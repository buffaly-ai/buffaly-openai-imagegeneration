# ImageGenerationHarnessModule.cs Change History

## Collapse Web Module Adapter Into Harness Module (2026-04-26)
- Converted `ImageGenerationHarnessModule` into the single `IBuffalyWebModule` implementation for OpenAI image generation.
- Kept install-artifact and host route mapping methods as no-ops because JSO/web server handling owns artifacts and hosted routes.
- Added `RegisterStandaloneRoutes(...)` so the local standalone harness can still register JsonWs routes without making hosted module startup map routes manually.
