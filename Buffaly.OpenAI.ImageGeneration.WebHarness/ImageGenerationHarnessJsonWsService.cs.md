# ImageGenerationHarnessJsonWsService.cs.md Change History


## Expose initializer route (2026-05-30)
- Added Initialize(ImageHarnessInitializeRequestContract) to store the OpenAIFeature-backed key supplied by the ProtoScript service before GetConfig/Generate/Edit calls use the harness runtime.

## Hydrate web UI config from OpenAIFeature (2026-05-30)
- Updated web-page JsonWs runtime creation to use `OpenAIFeature.Feature.ApiKey` when the process-local initializer state is empty.
- Design Decision: the Image Studio page calls JsonWs directly in the web process, which is separate from worker ProtoScript service state, so page config must use the authoritative feature directly to show the connected key state.

## Use default root for page config (2026-05-30)
- Updated runtime creation to use the harness default output root when `RootDirectory` is empty so the default Image Studio page `GetConfig` call returns config instead of failing.
- Preserved `OpenAIFeature.Feature.ApiKey` as the authoritative fallback key and did not restore legacy environment or appsettings key lookup.
