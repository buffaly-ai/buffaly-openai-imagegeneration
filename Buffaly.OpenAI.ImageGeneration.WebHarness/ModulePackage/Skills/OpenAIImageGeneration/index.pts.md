# OpenAIImageGeneration ModulePackage Skill

- Publishes the OpenAI image generation ProtoScript skill with the web harness module.
- The installed skill uses the named assembly reference `Buffaly.OpenAI.ImageGeneration`; the ProtoScript runtime resolves it from the environment's authoritative `lib/web-modules` install root.
- Uses ProtoScript string throws instead of `new Exception(...)` so the active staging runtime can compile the skill without requiring an `Exception` prototype/type.

- Simplified the skill so each action configures and calls the C# image runtime directly instead of routing through a ProtoScript Service wrapper, avoiding null service-call return propagation in staging.

- Aligned ProtoScript imports with OpenAIImageGenerationServiceRuntime facade methods so action calls bind to the actual C# runtime type.
