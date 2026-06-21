# Contributing to Ryx Sidekick

Thanks for your interest in improving Ryx Sidekick! This guide explains how to set up,
build, test, and submit changes.

## Contributor License Agreement (required)

Before we can merge your contribution, you must sign our
[Contributor License Agreement](CLA.md). A bot will prompt you to sign on your first pull
request. The CLA lets us keep Ryx Sidekick available under both the open-source GPL-3.0
license and our commercial editions.

## Requirements

- Unity **6000.0** or newer (Unity 6).
- A provider CLI installed (for example, the Claude CLI). See the
  [user guide](Documentation~/index.md).

## Setting up

1. Create or open a Unity 6 project.
2. Add the package via **Window → Package Manager → Add package from git URL** using
   `https://github.com/Vidosen/ryx-sidekick-lite.git`, or clone this repository into your project's
   `Packages/` folder.
3. Open **Window → Ryx Sidekick**.

## Project layout

The package follows a layered architecture (`Presentation → Application → Domain`, with
`Infrastructure` adapters). The Editor UI is built entirely with UI Toolkit (UXML + USS) on
top of Unity App UI. Please keep new code in the appropriate layer — layer boundaries are
enforced by tests.

## Tests

EditMode tests run through the Unity Test Runner: **Window → General → Test Runner →
EditMode tab → Run All**. Please add tests for new behavior and make sure the suite is green
before opening a pull request.

## Coding conventions

- Match the style of the surrounding code.
- Do not hand-write Unity `.meta` files — let the Editor generate them.
- Keep commits focused, with clear commit messages.
- Add a bullet under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for notable changes.

## Submitting a pull request

1. Fork the repository and create a feature branch.
2. Make your change, with tests.
3. Open a pull request describing what changed and why.
4. Sign the CLA when the bot prompts you.
