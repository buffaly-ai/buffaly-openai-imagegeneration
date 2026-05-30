# ImageHarnessRuntime.cs.md Change History


## Use service-initialized OpenAI key (2026-05-30)
- Removed direct OpenAIFeature access from the web harness runtime and added Create(apiKey, rootDirectory) so ProtoScript service initialization owns key retrieval.
