# dnSpy MCP Guide for Agents

This guide is for coding agents working in this repo that need to inspect or patch .NET assemblies through dnSpy MCP.

## Goal

Use the `user-dnspy` MCP server to:
- enumerate loaded assemblies,
- inspect namespaces/classes/methods,
- fetch C# or IL,
- apply targeted edits when explicitly requested.

## Preconditions

Before calling tools:
1. dnSpyEx is running with the MCP extension loaded.
2. The MCP server is reachable and connected as `user-dnspy`.
3. You have read the local tool descriptor JSON files first (required by this workspace policy).

## Hard Rules for Agents

- Call `Help` first in each new workflow/session.
- Prefer read-only discovery tools before any write operation.
- Do not modify IL or source until you have exact `Assembly` + `Namespace` + `ClassName` + `MethodName`.
- After any source/IL edit, call `Update_Tabs_View`.
- Treat write operations as high risk; summarize intended change and confirm scope first unless the user explicitly asked to apply it immediately.

## Tool Names and Parameter Conventions

The server mixes naming styles. Use the exact argument keys expected by each tool:

- `Get_Loaded_Assemblies`: no args
- `Namespaces_From_Assembly`: `{ "AssemblyName": "..." }`
- `Get_Global_Namespaces`: no args
- `Classes_From_Namespace`: `{ "AssemblyName": "...", "Namespace": "..." }`
- `Get_Class_Sourcecode`: `{ "Assembly": "...", "Namespace": "...", "ClassName": "..." }`
- `Get_Method_Prototypes`: `{ "Assembly": "...", "Namespace": "...", "ClassName": "..." }`
- `Get_Method_SourceCode`: `{ "Assembly": "...", "Namespace": "...", "ClassName": "...", "MethodName": "..." }`
- `Update_Method_SourceCode`: `{ "Assembly": "...", "Namespace": "...", "ClassName": "...", "MethodName": "...", "Source": "..." }`
- `Get_Function_Opcodes`: `{ "assemblyName": "...", "namespace": "...", "className": "...", "methodName": "..." }`
- `Set_Function_Opcodes`: `{ "assemblyName": "...", "namespace": "...", "className": "...", "methodName": "...", "ilOpcodes": ["..."], "ilLineNumber": 0, "mode": "Overwrite|Append" }`
- `Overwrite_Full_Func_Opcodes`: `{ "assemblyName": "...", "namespace": "...", "className": "...", "methodName": "...", "ilOpcodes": ["..."] }`
- `Rename_Namespace`: `{ "Assembly": "...", "Old_Namespace_Name": "...", "New_Namespace_Name": "..." }`
- `Rename_Class`: `{ "Assembly": "...", "Namespace": "...", "OldClassName": "...", "NewClassName": "..." }`
- `Rename_Method`: `{ "Assembly": "...", "Namespace": "...", "ClassName": "...", "MethodName": "...", "Newname": "..." }`
- `Update_Tabs_View`: no args

## Standard Discovery Workflow

Use this exact sequence for reverse-engineering tasks:

1. `Help`
2. `Get_Loaded_Assemblies`
3. `Namespaces_From_Assembly`
4. `Classes_From_Namespace`
5. `Get_Method_Prototypes`
6. `Get_Method_SourceCode` or `Get_Function_Opcodes`

This narrows safely from assembly to precise method target.

## Current Live Context (from this workspace's dnSpy session)

The loaded assembly list is Bannerlord-heavy and includes:
- `TaleWorlds.CampaignSystem.ViewModelCollection`
- `TaleWorlds.CampaignSystem`
- `TaleWorlds.MountAndBlade`
- `SandBox`
- `StoryMode`
- `Bannerlord.UIExtenderEx`
- framework/system assemblies (`mscorlib`, `System.Core`, `Newtonsoft.Json`, etc.)

Verified encyclopedia target path in loaded game DLLs:
- Assembly: `TaleWorlds.CampaignSystem.ViewModelCollection`
- Namespace: `TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages`
- Class: `EncyclopediaHeroPageVM`
- Example methods discovered: `Refresh`, `RefreshValues`, `UpdateInformationText`, `ExecuteSwitchBookmarkedState`

## Copy-Paste MCP Call Examples

### 1) List loaded assemblies

```json
{
  "server": "user-dnspy",
  "toolName": "Get_Loaded_Assemblies"
}
```

### 2) Enumerate encyclopedia namespaces

```json
{
  "server": "user-dnspy",
  "toolName": "Namespaces_From_Assembly",
  "arguments": {
    "AssemblyName": "TaleWorlds.CampaignSystem.ViewModelCollection"
  }
}
```

### 3) List encyclopedia page classes

```json
{
  "server": "user-dnspy",
  "toolName": "Classes_From_Namespace",
  "arguments": {
    "AssemblyName": "TaleWorlds.CampaignSystem.ViewModelCollection",
    "Namespace": "TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages"
  }
}
```

### 4) Get method prototypes for `EncyclopediaHeroPageVM`

```json
{
  "server": "user-dnspy",
  "toolName": "Get_Method_Prototypes",
  "arguments": {
    "Assembly": "TaleWorlds.CampaignSystem.ViewModelCollection",
    "Namespace": "TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages",
    "ClassName": "EncyclopediaHeroPageVM"
  }
}
```

### 5) Fetch C# for `Refresh`

```json
{
  "server": "user-dnspy",
  "toolName": "Get_Method_SourceCode",
  "arguments": {
    "Assembly": "TaleWorlds.CampaignSystem.ViewModelCollection",
    "Namespace": "TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages",
    "ClassName": "EncyclopediaHeroPageVM",
    "MethodName": "Refresh"
  }
}
```

### 6) Fetch IL for `ExecuteSwitchBookmarkedState`

```json
{
  "server": "user-dnspy",
  "toolName": "Get_Function_Opcodes",
  "arguments": {
    "assemblyName": "TaleWorlds.CampaignSystem.ViewModelCollection",
    "namespace": "TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages",
    "className": "EncyclopediaHeroPageVM",
    "methodName": "ExecuteSwitchBookmarkedState"
  }
}
```

## Safe Edit Workflow (Source)

1. Read current method using `Get_Method_SourceCode`.
2. Prepare replacement body in valid C#.
3. Apply with `Update_Method_SourceCode`.
4. Run `Update_Tabs_View`.
5. Re-read method source (or IL) to verify.

## Safe Edit Workflow (IL)

1. Read current IL with `Get_Function_Opcodes`.
2. Prefer `Set_Function_Opcodes` with small `Append`/`Overwrite` deltas.
3. Use `Overwrite_Full_Func_Opcodes` only when intentional full replacement is required.
4. Run `Update_Tabs_View`.
5. Re-read IL and check control flow/stack safety.

## Common Failure Modes

- Empty or unexpected output:
  - Verify target name casing and exact namespace/class string.
  - Re-run discovery chain (assembly -> namespace -> class -> method).
- Wrong argument key names:
  - Check tool-specific key style (`Assembly` vs `AssemblyName` vs `assemblyName`).
- Edits appear not applied in UI:
  - Call `Update_Tabs_View`.
- Method mismatch/overload confusion:
  - Use `Get_Method_Prototypes` first, then pick exact target.

## Recommended Agent Checklist

- [ ] Called `Help`
- [ ] Confirmed assembly exists in `Get_Loaded_Assemblies`
- [ ] Located exact namespace/class/method via discovery
- [ ] Captured pre-edit source/IL
- [ ] Applied smallest possible change
- [ ] Refreshed tabs
- [ ] Re-queried source/IL to verify result

## Reference

- Upstream dnSpy MCP extension: https://github.com/AgentSmithers/DnSpy-MCPserver-Extension

