# AB-400 Power Platform Developer Labs

Hands-on labs built while preparing for Microsoft exam **AB-400: Extending Microsoft Power Platform Solutions with Code and AI** (Power Platform Developer Associate). Each lab is real code deployed to and tested in a Dataverse developer environment, not a tutorial copy.

| Lab | Topic | Status |
| --- | --- | --- |
| [W01](src/Ab4.Plugins) | Dataverse plug-ins: event pipeline, execution context, entity images, Plug-in Registration Tool | Complete |

## W01: Dataverse plug-ins

A NuGet **plug-in package** (`pac plugin init`, .NET Framework 4.6.2) with four plug-ins on the `account` table, each chosen to exercise a different pipeline stage and design decision.

| Plug-in | Message / stage / mode | Demonstrates |
| --- | --- | --- |
| `CreditLimitGuard` | Create + Update, PreValidation (10), sync | Cancelling an operation before the transaction with `InvalidPluginExecutionException`; per-step unsecure configuration; filtering attributes |
| `AccountNumberDefaulter` | Create, PreOperation (20), sync | Modifying the `Target` in-flight so the platform's own write persists it, with no extra service call |
| `CreditLimitChangeAudit` | Update, PostOperation (40), sync | Pre and post entity images, Organization service `Create`, depth guard, and a switchable forced failure that proves transactional rollback |
| `AccountFollowUp` | Create, PostOperation (40), async | Reading `OutputParameters["id"]`, asynchronous execution as a system job, and a full execution-context dump to the trace log |

### Design notes

- **Plain `IPlugin`, not a generated base class**, so every service lookup (`IPluginExecutionContext`, `ITracingService`, `IOrganizationServiceFactory`) is explicit.
- **Stateless classes.** Only immutable configuration is held in fields; context and services are resolved per execution because Dataverse caches plug-in instances.
- **Self-validating registration.** Each plug-in checks its own stage, message and mode and fails loudly if registered on the wrong step.
- **Images over retrieves.** The audit step reads before/after values from narrow entity images instead of querying the record.
- **Filtering attributes** restrict the Update steps to `creditlimit`, and the audit still compares images because filters fire whenever the column is submitted, changed or not.

### Verified behaviour

| Test | Result |
| --- | --- |
| Create account | Account number generated in PreOperation; follow-up task created asynchronously |
| Credit limit over the configured ceiling (Create and Update) | Blocked in PreValidation; no downstream steps run |
| Credit limit change | Audit note records old → new value from entity images |
| Forced failure after the audit note is written | Note and update both roll back; confirmed via Web API lookup of the note ID |
| Trace log review | `IsInTransaction`, `Mode`, `Depth`, timing and exception details confirmed through `plugintracelogs` |

### Build and deploy

```powershell
cd src/Ab4.Plugins
dotnet build -c Debug            # produces bin/Debug/Ab4.Plugins.<version>.nupkg
pac tool prt                     # register the package, steps and images
pac plugin push --pluginId <pluginpackageid> --pluginFile <path to .nupkg> --type Nuget   # subsequent updates
```

The .NET Framework reference assemblies come from the `Microsoft.NETFramework.ReferenceAssemblies` NuGet package, so no Developer Pack install is needed.

## Tooling

C# / .NET Framework 4.6.2 · Power Platform CLI (`pac`) · Plug-in Registration Tool · Dataverse Web API · VS Code
