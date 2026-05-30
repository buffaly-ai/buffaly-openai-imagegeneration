# Buffaly.OpenAI.ImageGeneration.WebHarness.csproj Change History
## Publish web-module skill artifacts (2026-04-26)
- Added `Skills\**\*` as publish content so the provisioning published-folder installer can copy the OpenAI image generation ProtoScript skill into the installed OpsAgent project without a custom packaging step.
- Design: keep the developer workflow as plain `dotnet publish`; the installer owns final placement of module binaries, JsonWs stubs, web assets, and skill files.

## Published web-module installer (2026-04-26)
- Added publish content for the OpenAI image generation ProtoScript skill under `ModulePackage\Skills\OpenAIImageGeneration` so normal `dotnet publish` output can be consumed by the provisioning web-module installer.

## Host web-module contract reference (2026-04-27)
- Replaced the fragile absolute `ProjectReference` to `Buffaly.Agent.Web.Common.csproj` with a compile-time `Reference` to `C:\dev\Buffaly.Development\Deploy\Buffaly.Agent.Web.Common.dll`, matching the deployed Buffaly host contract assembly.
- The publish target still deletes `Buffaly.Agent.Web.Common.dll` so installed web modules bind to the host's contract assembly instead of deploying a private copy.

## Publish generated JsonWs metadata (2026-05-30)
- Included Generated/JsonWs metadata in published module output so the initialize route is installed with the web module.
