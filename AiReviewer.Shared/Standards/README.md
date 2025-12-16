# NNF Standards Storage Strategy

## Overview

The AI Reviewer uses a **3-layer configuration merge** system for coding standards:

```
┌─────────────────────────────────────────────────────────────┐
│                    FINAL EFFECTIVE CONFIG                    │
│         (What the AI reviewer actually uses)                 │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ merge (override)
┌─────────────────────────────────────────────────────────────┐
│  Layer 3: REPO-SPECIFIC RULES                               │
│  Location: .config/stagebot/PullRequestAssistant.yaml       │
│  Purpose: Team/repo-specific overrides and additions        │
│  Managed by: Repository owners via PR                       │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ merge (override)
┌─────────────────────────────────────────────────────────────┐
│  Layer 2: NNF STANDARDS (API)                               │
│  Location: Azure Table Storage via Functions API            │
│  File: Standards/NnfCodingStandards.yaml                    │
│  Purpose: Organization-wide NNF coding guidelines           │
│  Managed by: NNF Architects/Leads via API                   │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ merge (base)
┌─────────────────────────────────────────────────────────────┐
│  Layer 1: EMBEDDED STANDARDS (Fallback)                     │
│  Location: EmbeddedStandards.cs (compiled into DLL)         │
│  Purpose: Baseline standards when API unavailable           │
│  Managed by: Extension developers                           │
└─────────────────────────────────────────────────────────────┘
```

## File Locations

### 1. NnfCodingStandards.yaml (THIS FILE)
- **Path**: `AiReviewer.Shared/Standards/NnfCodingStandards.yaml`
- **Purpose**: Source of truth for NNF coding standards
- **Contains**: 75+ checks covering all NNF guidelines
- **Usage**: 
  - Upload via API: `PUT /api/standards/nnf`
  - Can be edited and re-uploaded when guidelines change

### 2. Azure Table Storage
- **Table**: `NnfStandards`
- **Schema**:
  ```
  PartitionKey: "NNF"
  RowKey: "current" | "v1" | "v2" | "v{n}"
  YamlContent: string (the full YAML)
  Version: int
  UpdatedBy: string
  UpdatedAt: DateTime
  ChangeDescription: string
  ```

### 3. API Endpoints
- `GET /api/standards/nnf` - Fetch current standards
- `PUT /api/standards/nnf` - Update standards (creates version history)
- `GET /api/standards/nnf/history` - Get version history

## How to Update NNF Standards

### Option 1: Via API (Recommended)
```bash
# Read the YAML file
$yaml = Get-Content -Path "AiReviewer.Shared/Standards/NnfCodingStandards.yaml" -Raw

# Call the API
Invoke-RestMethod -Uri "https://your-function.azurewebsites.net/api/standards/nnf" `
    -Method PUT `
    -Headers @{ "Authorization" = "Bearer $token" } `
    -Body (@{
        yamlContent = $yaml
        updatedBy = "your-email@microsoft.com"
        changeDescription = "Updated with v1.0 NNF guidelines"
    } | ConvertTo-Json) `
    -ContentType "application/json"
```

### Option 2: Via VS Code Extension
The extension could have a command to push standards:
```
Command: AI Reviewer: Update NNF Standards
```

## YAML Structure

```yaml
metadata:
  name: "NNF Coding Guidelines"
  version: "1.0"
  description: "..."

categories:
  - documentation
  - access-modifiers
  # ... more categories

checks:
  - id: nnf-doc-001
    name: "Class must have XML summary"
    description: "Every class must have a <summary>..."
    category: documentation
    severity: warning  # error | warning | info
    example: |
      /// <summary>
      /// ...
      /// </summary>
```

## Categories in NnfCodingStandards.yaml

| Category | Count | Description |
|----------|-------|-------------|
| documentation | 4 | XML docs for classes/methods |
| access-modifiers | 4 | public/private/internal guidance |
| method-implementation | 6 | Code structure, line limits |
| exception-handling | 6 | ServiceException, cancellation |
| logging | 4 | Log levels, PII protection |
| telemetry | 5 | OpenTelemetry, metrics |
| general-principles | 4 | DRY, YAGNI, KISS |
| comments | 3 | What vs Why comments |
| collections | 2 | Empty objects, readonly |
| classes-structs | 5 | sealed, struct, interfaces |
| members-accessibility | 3 | Least privilege |
| usage-patterns | 4 | Defaults, FirstOrDefault |
| naming-conventions | 2 | OrDefault, TryXxx |
| async-patterns | 3 | await vs Result |
| null-handling | 7 | Nullable, validation |
| equality | 3 | IEquatable, GetHashCode |
| conditionals | 2 | Guard clauses |
| formatting | 4 | Line breaks, arrow operator |
| language-features | 7 | No var, const vs readonly |
| project-organization | 5 | Namespaces, one file per type |
| dependency-injection | 3 | DI class, TryAdd |
| configuration | 6 | IOptions, timeouts |
| error-handling | 2 | Polly |
| testing | 10 | xUnit best practices |

**Total: 75+ checks**

## Severity Levels

- **error**: Must fix before merge (blocking)
- **warning**: Should fix, but not blocking
- **info**: Best practice suggestion

## Next Steps

1. **Deploy the YAML**: Upload `NnfCodingStandards.yaml` to Azure via the API
2. **Verify**: Call `GET /api/standards/nnf` to confirm it's stored
3. **Test**: Run a review on sample code to see the checks in action
4. **Iterate**: Update the YAML as NNF guidelines evolve
