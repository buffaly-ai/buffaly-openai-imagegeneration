# OpenAIImageGenerationFacade.cs Change History

## Add service runtime contract (2026-04-26)
- Replaced the earlier per-call facade shape with `OpenAIImageGenerationServiceRuntime`, configured once with OpenAI API key, output directory, and artifact-relative directory.
- Added create/edit request contracts and result contract that returns physical and session-relative artifact paths.
- Create/edit methods now throw for invalid configuration, invalid requests, failed OpenAI operation payloads, or malformed response JSON.

## Add ProtoScript-safe primitive facade methods (2026-05-08)
- Added `CreateImage(...)` and `EditImage(...)` facade methods that accept primitive strings, configure the runtime, build request contracts in C#, and return the serialized operation result string.
- Design decision: keep ProtoScript as a thin pass-through and avoid object-construction/static-call return binding issues in the script layer.
