# Contributors guidelines

## Reporting Bugs

Please follow these guidelines when reporting a bug or crash:
* Please don't report bugs with games or addons - report them to the author.
* Please be thorough in your bug report. Tell us everything.
* If you can recreate the bug give us a step by step.
* Make sure your bug hasn't already been reported.

### Proton

For compatibility issues with Proton refer to [this issue](https://github.com/ValveSoftware/Proton/issues/4940).

## Feature Requests

Please follow these guidelines when requesting a feature:
* Tell us why you need this feature, what does it add, what have you tried.
* Make sure your feature hasn't already been requested.

## Security Exploits

If you've found a security exploit, please report it by visiting https://facepunch.com/security

## Making Changes

### Adding new features

Before you start trying to add a new feature, it should be something people want and has been discussed in a proposal issue ideally.

### Fixing bugs

If you're fixing a bug, make sure you reference any applicable bug reports, explain what the problem was and how it was solved.

Unit tests are always great where applicable.

### Guidelines

A few guidelines that will make it easier to review and merge your changes:

* **Scope**
    * Keep your pull requests in scope and avoid unnecessary changes.
* **Commits**
    * Should group relevant changes together, the message should explain concisely what it's doing, there should be a longer summary elaborating if required.
    * Remove unnecessary commits and squash commits together where appropriate.
* **Formatting**
    * Your IDE should adhere to the style set in `.editorconfig`
    * Auto formatting can be done with `dotnet format`