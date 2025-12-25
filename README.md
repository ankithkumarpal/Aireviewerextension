# AI Stage Reviewer for Visual Studio

**AI-Powered Code Review** — Get instant code review feedback on your staged changes before you commit!
---

## How It Works

1. **Stage your changes** in Git
2. **Click "Review Staged Changes"** in Visual Studio
3. **Authenticate** with Azure AD (enterprise security)
4. **Get instant feedback** with severity levels and confidence scores
5. **Apply fixes** with one click or provide feedback to improve the AI
---

## Requirements

- Visual Studio 2022 (17.0 or higher)
- Git repository with staged changes
- Azure AD account (authorized by administrator)

---

## Installation

1. Download the extension from Releases : https://marketplace.visualstudio.com/items?itemName=AnkithPal.ai-code-reviewer-local
2. Restart Visual Studio
3. Open **View → Other Windows → AI Code Reviewer**

---

## Configuration

### Repository Rules (Optional)

Create `.config/stagebot/stagebot.yaml` in your repository to define custom rules:

```yaml
checks:
  - id: repo-no-hardcoded-secrets
    applies_to: ["*.cs"]
    severity: High
    description: "No hardcoded passwords or secrets"
    guidance: "Use Azure Key Vault or configuration"
```
---

## Usage

1. **Stage changes** — `git add` your modified files
2. **Open AI Code Reviewer** — Tools → Review Stage changes
3. **Click "Review Staged Changes"**
4. **Review findings** — Each shows severity, confidence, and suggested fix
5. **Apply fixes** — Click "Apply Fix" for one-click corrections
6. **Provide feedback** — Mark suggestions as helpful or not helpful

---

## Supported Languages

- C# (.cs files)
- .NET projects

---

## Privacy & Security

- **Azure AD authentication** — Only authorized users can access
- **Auditable** — Every action is tied to user identity
- **Your code is analyzed by Azure OpenAI** — No data stored permanently

---

## Contributing

Found a bug or have a feature request? Please open an issue!

---

## License

MIT License — See [LICENSE.txt](LICENSE.txt) for details.
