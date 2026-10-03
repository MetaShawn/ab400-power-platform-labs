# AB-400 Power Platform Developer Labs

Hands-on labs built while preparing for Microsoft exam **AB-400: Extending Microsoft Power Platform Solutions with Code and AI** (Power Platform Developer Associate). Each lab is real code deployed to and tested in a Dataverse developer environment, not a tutorial copy.

| Lab | Topic | Status |
| --- | --- | --- |
| [W01](src/Ab4.Plugins) | Dataverse plug-ins: event pipeline, execution context, entity images, Plug-in Registration Tool | Complete |
| [W02](#w02-custom-apis-and-business-events) | Custom APIs, plug-ins that implement them, Dataverse business events | Complete |

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

## W02: Custom APIs and business events

Three custom APIs on top of the same plug-in package, each created a different way and each chosen to hit a different design decision. The request API decides inside Dataverse; anything above its auto-approve ceiling is published as a **business event** that a cloud flow subscribes to after the transaction commits.

| Custom API | Kind / binding | Custom processing steps | Main operation | Created with |
| --- | --- | --- | --- | --- |
| `ab4_RequestCreditIncrease` | Action, bound to `account` | Sync and Async | `RequestCreditIncreaseApi` | Plug-in Registration Tool |
| `ab4_GetCreditHeadroom` | Function (GET), global | None | `GetCreditHeadroomApi` | Web API deep insert ([script](scripts/W02-Setup-EventAndCatalog.ps1)) |
| `ab4_OnCreditIncreaseRequested` | Action, global, business event | Async Only | none | Web API deep insert |

The event is cataloged (`AB400 Lab` → `Credit Events`) so it appears in the Power Automate Dataverse trigger **When an action is performed**; the flow creates an approval task from the event's `ActionInputs`.

### Design notes

- **Main-operation plug-ins (stage 30)** read request parameters from `InputParameters` and write response properties to `OutputParameters`. Optional parameters are read with `TryGetValue`.
- **No synchronous logic on the event.** The event API has no plug-in, no response properties and only allows asynchronous steps, so nothing in Dataverse can fail the caller that raises it, whether that's the request API or an external system calling it directly.
- **Shared constants, no configuration.** A custom API's main operation can't receive secure or unsecure configuration, so the ceiling lives in `CreditPolicy`; it moves to an environment variable in a later lab.
- **Least-privilege call.** The bound action requires `prvWriteAccount` through *Execute Privilege Name*.
- **Idempotent provisioning.** The setup script creates the function, event API, two-level catalog and assignments in the `AB400` solution with the `MSCRM.SolutionUniqueName` header, and skips anything that already exists.

### Verified behaviour

[`W02-Test-CustomApis.ps1`](scripts/W02-Test-CustomApis.ps1) exercises everything through the Web API.

| Test | Result |
| --- | --- |
| Function, before and after an increase | Returns current limit, ceiling and headroom; reflects the update |
| Request at or below the ceiling | Approved; account updated through the Organization service |
| Request above the ceiling | Not approved, account unchanged, business event emitted; flow creates an approval task |
| Request below the current limit | HTTP 400 carrying the plug-in's `InvalidPluginExecutionException` message |
| Bound action called without the `accounts(id)` segment | HTTP 404: binding is part of the address |
| Event API called directly (external-system pattern) | HTTP 204; flow creates a second approval task with no plug-in involved |
| Sync step registration on the Async Only event | Refused by Dataverse |
| Trace log review | Both main operations run with `IsInTransaction=True`, including the GET function |

**Finding:** updates made through the custom API run at `Depth=2`, so the W01 audit plug-in (which only reacts at depth 1) skips them, while the PreValidation guard still applies. Depth says who called, not what changed; the audit should key off its pre/post images instead.

## Tooling

C# / .NET Framework 4.6.2 · Power Platform CLI (`pac`) · Plug-in Registration Tool · Dataverse Web API · PowerShell 7 + Azure CLI · Power Automate · VS Code
