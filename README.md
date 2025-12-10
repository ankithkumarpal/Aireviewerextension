# AI Code Reviewer for Visual Studio 2022

**AI-Powered Code Review** - Get instant, comprehensive code review feedback on your staged changes before you commit!

## Features

✨ **Comprehensive Review Categories:**
- 🔒 **Security** - SQL injection, hardcoded secrets, auth issues
- ⚡ **Performance** - N+1 queries, inefficient algorithms, missing async/await
- 🛡️ **Reliability** - Null checks, error handling, resource leaks
- 🎯 **Code Quality** - Console.WriteLine detection, magic numbers, code duplication
- 📝 **Best Practices** - SOLID principles, proper logging, naming conventions

✨ **Smart Features:**
- **One-Click Fixes** - Apply AI-suggested corrections instantly
- **Context-Aware** - Understands your codebase patterns
- **Project-Specific Rules** - Integrates with MerlinBot configuration
- **Modern UI** - Clean WPF tool window with categorized results

## How It Works

1. **Stage your changes** with `git add`
2. **Click "Review Staged Changes"** from the Tools menu
3. **Get instant feedback** with severity levels (High/Medium/Low)
4. **Apply fixes** with one click or navigate to issues

## Requirements

- Visual Studio 2022 (17.0 or higher)
- Git repository with staged changes
- Azure OpenAI API key (for AI-powered analysis)

## Installation

1. Download the `.vsix` file from [Releases](https://github.com/ankithkumarpal/Aireviewerextension/releases)
2. Double-click to install
3. Restart Visual Studio
4. Configure your Azure OpenAI credentials in `.config/ai-reviewer/ai-reviewer-config.yaml`

## Configuration

Create `.config/ai-reviewer/ai-reviewer-config.yaml` in your repository root:

```yaml
# AI Reviewer Configuration
# Azure OpenAI credentials

azureOpenAIEndpoint: https://your-instance.openai.azure.com/
azureOpenAIKey: your-api-key-here
deploymentName: gpt-4o-mini
```

## Usage

### Menu Commands
- **Tools → AI Code Reviewer → Review Staged Changes** - Analyze staged git changes
- **View → Other Windows → AI Code Reviewer** - Open the results window

### Keyboard Shortcuts
You can assign custom shortcuts in Tools → Options → Keyboard

## Screenshots

![AI Code Review Results](https://via.placeholder.com/800x400?text=AI+Code+Review+Results)

## Supported Languages

Currently optimized for:
- C#
- .NET projects

## Privacy & Security

- Your code is sent to Azure OpenAI for analysis
- API keys are stored locally and never committed
- No telemetry or data collection

## Contributing

Found a bug or have a feature request? Please open an issue!

## License

MIT License - See LICENSE file for details

## Author

**Ankith Pal**
- GitHub: [@ankithkumarpal](https://github.com/ankithkumarpal)
- LinkedIn: [ankithpal](https://linkedin.com/in/ankithpal)

## Hackathon Project

Built for **LIVE+ Network Fabric Hackathon 2025**

---

** If you find this useful, please star the repository!**
