// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEditor;

namespace Ryx.Sidekick.Editor.Infrastructure.Assets
{
    /// <summary>
    /// Represents a search result for the @ mention asset picker.
    /// </summary>
    internal class AssetSearchResult
    {
        public string AssetPath { get; set; }
        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public bool IsPrefab { get; set; }
        public int Score { get; set; }
    }

    /// <summary>
    /// Service for searching project assets with adaptive filtering and scoring.
    /// Used by the @ mention system to find relevant files and prefabs.
    /// </summary>
    internal static class ProjectAssetSearchService
    {
        private const int MaxResults = 15;
        private const int ScorePrefixMatch = 100;
        private const int ScoreContainsMatch = 50;
        private const int ScoreSubsequenceMatch = 25;
        private const int ScoreIsPrefab = 10;
        private const int ScoreIsScript = 5;

        /// <summary>
        /// Searches for assets matching the query.
        /// Returns text files supported by ContextAttachmentService plus prefabs.
        /// </summary>
        /// <param name="query">Search query (can be filename, partial path, etc.)</param>
        /// <returns>Scored and sorted list of matching assets.</returns>
        public static List<AssetSearchResult> Search(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                // Return recent/common assets when no query
                return GetDefaultResults();
            }

            // Normalize query: extract last segment if it looks like a path
            var normalizedQuery = NormalizeQuery(query);
            
            // Use AssetDatabase for fast indexed search
            var guids = AssetDatabase.FindAssets(normalizedQuery);
            
            var results = new List<AssetSearchResult>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                
                // Skip duplicates
                if (seenPaths.Contains(path))
                    continue;
                
                // Filter: only text files and prefabs
                if (!IsAllowedAsset(path))
                    continue;

                seenPaths.Add(path);
                
                var fileName = Path.GetFileName(path);
                var isPrefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                
                var result = new AssetSearchResult
                {
                    AssetPath = path,
                    FileName = fileName,
                    DisplayName = fileName,
                    IsPrefab = isPrefab,
                    Score = CalculateScore(path, fileName, normalizedQuery, isPrefab)
                };
                
                results.Add(result);
            }

            // Sort by score descending, then by filename
            return results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.FileName)
                .Take(MaxResults)
                .ToList();
        }

        /// <summary>
        /// Checks if the asset is allowed in @ mention search results.
        /// </summary>
        private static bool IsAllowedAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Allow prefabs
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return true;

            // Allow text files recognized by ContextAttachmentService
            return ContextAttachmentService.IsTextFile(path);
        }

        /// <summary>
        /// Normalizes the search query for better matching.
        /// </summary>
        private static string NormalizeQuery(string query)
        {
            if (string.IsNullOrEmpty(query))
                return query;

            query = query.Trim();
            
            // If query contains path separator, take the last segment for fuzzy matching
            // but keep the original for path-based scoring
            var lastSlash = query.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < query.Length - 1)
            {
                return query.Substring(lastSlash + 1);
            }

            return query;
        }

        /// <summary>
        /// Calculates relevance score for an asset.
        /// Higher scores indicate better matches.
        /// </summary>
        private static int CalculateScore(string path, string fileName, string query, bool isPrefab)
        {
            var score = 0;
            var lowerFileName = fileName.ToLowerInvariant();
            var lowerFileNameNoExt = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            var lowerPath = path.ToLowerInvariant();
            var lowerQuery = query.ToLowerInvariant();

            // Exact filename match (without extension)
            if (lowerFileNameNoExt == lowerQuery)
            {
                score += ScorePrefixMatch * 2;
            }
            // Prefix match on filename (without extension)
            else if (lowerFileNameNoExt.StartsWith(lowerQuery))
            {
                score += ScorePrefixMatch;
            }
            // Contains match on filename
            else if (lowerFileName.Contains(lowerQuery))
            {
                score += ScoreContainsMatch;
            }
            // Contains match on path
            else if (lowerPath.Contains(lowerQuery))
            {
                score += ScoreSubsequenceMatch;
            }
            // Subsequence match (fuzzy)
            else if (IsSubsequence(lowerQuery, lowerFileNameNoExt))
            {
                score += ScoreSubsequenceMatch / 2;
            }

            // Bonus for prefabs (often more relevant as context)
            if (isPrefab)
            {
                score += ScoreIsPrefab;
            }

            // Bonus for scripts
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                score += ScoreIsScript;
            }

            // Penalty for very long paths (deeply nested files)
            var depth = path.Count(c => c == '/');
            if (depth > 5)
            {
                score -= (depth - 5) * 2;
            }

            return Math.Max(0, score);
        }

        /// <summary>
        /// Checks if query is a subsequence of target (for fuzzy matching).
        /// </summary>
        private static bool IsSubsequence(string query, string target)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            if (string.IsNullOrEmpty(target))
                return false;

            var queryIndex = 0;
            foreach (var c in target)
            {
                if (c == query[queryIndex])
                {
                    queryIndex++;
                    if (queryIndex >= query.Length)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns default results when no query is provided.
        /// Prioritizes scripts and prefabs from common locations.
        /// </summary>
        private static List<AssetSearchResult> GetDefaultResults()
        {
            var results = new List<AssetSearchResult>();
            
            // Search for scripts in Assets folder
            var scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            foreach (var guid in scriptGuids.Take(10))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(path);
                results.Add(new AssetSearchResult
                {
                    AssetPath = path,
                    FileName = fileName,
                    DisplayName = fileName,
                    IsPrefab = false,
                    Score = 50
                });
            }

            // Add some prefabs
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (var guid in prefabGuids.Take(5))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                var fileName = Path.GetFileName(path);
                results.Add(new AssetSearchResult
                {
                    AssetPath = path,
                    FileName = fileName,
                    DisplayName = fileName,
                    IsPrefab = true,
                    Score = 40
                });
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.FileName)
                .Take(MaxResults)
                .ToList();
        }
    }
}
