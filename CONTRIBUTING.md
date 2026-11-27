# Contributing to Running Orders

:+1: :tada: First off, thanks for taking the time to contribute! :tada: :+1:

The following is a set of guidelines for contributing to Running Orders.

## How Can I Contribute?

### Reporting Bugs

This section guides you through submitting a bug report for Running Orders. Following these guidelines helps maintainers and the community understand your report :pencil:, reproduce the behavior :computer:, and find related reports :mag_right:.

Before creating bug reports, please check [this list](#before-submitting-a-bug-report) as you might find out that you don't need to create one. When you are creating a bug report, please [include as many details as possible](#how-do-i-submit-a-good-bug-report). Fill out [the required template](https://github.com/Tetz95/RunningOrders/blob/main/.github/ISSUE_TEMPLATE/bug_report.md), the information it asks for helps us resolve issues faster.

> **Note:** If you find a **Closed** issue that seems like it is the same thing that you're experiencing, open a new issue and include a link to the original issue in the body of your new one.


#### Before Submitting A Bug Report

- **Check if you can reproduce the problem [in the latest version of Running Orders](https://github.com/Tetz95/RunningOrders/releases)**.
- **Ask in the modding channels** of the **[official Railroader Discord server](https://discord.gg/KpVkaDM7Nb)** if you're not sure it's a bug.
- **Perform a [cursory search](https://github.com/Tetz95/RunningOrders/issues)** to see if the problem has already been reported. If it has **and the issue is still open**, add a comment to the existing issue instead of opening a new one.

#### How Do I Submit A Good Bug Report?
Bugs are tracked as [GitHub issues](https://guides.github.com/features/issues/). Create an issue on the repository and provide the following information by filling in [the template](https://github.com/Tetz95/RunningOrders/blob/main/.github/ISSUE_TEMPLATE/bug_report.md).

- Use a clear and descriptive title for the issue to identify the problem
- A clear and concise description of what the bug is.
- Detailed steps to reproduce the behavior
- A clear and concise description of what you expected to happen.
- Add screenshots or video to help explain your problem. This helps immensely!
- Attach your **Player.log** file immediately after reproducing the bug.
- **If you are using Railloader**, attach your **Railloader.log** file as well.

> The **Player.log** file is found at ``C:\Users\YourUsername\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log``. The game starts a new one at each launch, so copy it right after the problem, before starting the game again.

> The **Railloader.log** file is located in your Railroader game install directory where Railroader.exe is located.

### Suggesting Enhancements

This section guides you through submitting an enhancement suggestion for Running Orders, including completely new features and minor improvements to existing functionality. Following these guidelines helps maintainers and the community understand your suggestion :pencil: and find related suggestions :mag_right:.

Before creating enhancement suggestions, please check [this list](#before-submitting-an-enhancement-suggestion) as you might find out that you don't need to create one. When you are creating an enhancement suggestion, please [include as many details as possible](#how-do-i-submit-a-good-enhancement-suggestion). Fill in [the template](https://github.com/Tetz95/RunningOrders/blob/main/.github/ISSUE_TEMPLATE/feature_request.md), including the steps that you imagine you would take if the feature you're requesting existed.

#### Before Submitting An Enhancement Suggestion

* **Check the [official Railroader Discord](https://discord.gg/KpVkaDM7Nb)** for tips — you might discover that the enhancement is already available. Most importantly, check if you're using [the latest version of Running Orders](https://github.com/Tetz95/RunningOrders/releases/latest) and if you can get the desired behavior by changing Running Orders's settings (the game's Settings window, *Running Orders* > *Open settings*).
* **Perform a [cursory search](https://github.com/Tetz95/RunningOrders/issues)** to see if the enhancement has already been suggested. If it has, add a comment to the existing issue instead of opening a new one.

#### How Do I Submit A Good Enhancement Suggestion?

Enhancement suggestions are tracked as [GitHub issues](https://guides.github.com/features/issues/). Create an issue on the repository and provide the following information:

* **Use a clear and descriptive title** for the issue to identify the suggestion.
* **Provide a step-by-step description of the suggested enhancement** in as many details as possible.
* **Provide specific examples to demonstrate the steps**.
* **Include screenshots, videos, or diagrams** which help you demonstrate the steps or point out the part of Running Orders which the suggestion is related to.
* **Explain why this enhancement would be useful** to most Running Orders users.
* **Specify which version of Running Orders you're using.**

### Pull Requests

This section guides you through submitting a pull request for Running Orders. The process described here has several goals:

- Maintain Running Orders's quality
- Fix problems that are important to users
- Engage the community in working toward the best possible Running Orders
- Enable a sustainable system for Running Orders's maintainers to review contributions


#### Before Submitting A Pull Request

- **Before you get started**, [look for an issue](https://github.com/Tetz95/RunningOrders/issues) you want to work on. Please make sure this issue is not currently assigned to someone else. If there's no existing issue for your idea, please open one following the above guidelines for [reporting bugs](https://github.com/Tetz95/RunningOrders/blob/main/CONTRIBUTING.md#reporting-bugs) and [suggesting enhancements](https://github.com/Tetz95/RunningOrders/blob/main/CONTRIBUTING.md#suggesting-enhancements).
- **Once you select an issue**, please discuss your idea for the fix or implementation in the comments for that issue. There are often several ways to fix a problem and it's possible that someone else may have a plan for the issue, or that there's context you should know before starting implementation.
- **After the approach has been discussed** and everything looks good, you can begin local development. Please don't start development work without discussing it first. Otherwise, you might waste time on an issue if work is already being done on it, or if there's something else in the way!

#### How Do I Submit A Good Pull Request?

Please follow these steps to have your contribution considered by the maintainers:

1. Follow all instructions in [the template](pull_request_template.md)
2. Follow the [styleguides](#styleguides)

While the prerequisites above must be satisfied prior to having your pull request reviewed, the reviewer(s) may ask you to complete additional design work, tests, or other changes before your pull request can be ultimately accepted.

> This [recommended article](https://mtlynch.io/code-review-love/) describes best practices for participating in a code review when you're the author. The most important recommendation to remember while still developing the code is to **narrowly scope changes**. The smaller and simpler the change, the easier and faster it is to review.

## Styleguides

### Git Commit Messages

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests liberally after the first line

### C# Styleguide

In general, use [Microsoft's C# styleguide and code conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).

Lint all C# code using Visual Studio's Code Cleanup tool with the following default fixers enabled:
- Format document
- Remove unncessary imports or usings
- Sort Imports or usings
- Apply file header preferences