# Sanitized Export Report

Source: C:\dev\buffaly.tools\buffaly.openai.imagegeneration
Destination: C:\dev\buffaly-ai\buffaly-openai-imagegeneration
Solution: 
Included files: 34
Excluded files: 532
Included allowed binaries: 0
Manual review candidates: 2
Secret pattern hits: 8

## Included Allowed Binaries
None.

## Manual Review Candidates
- Buffaly.OpenAI.ImageGeneration.WebHarness\Properties\launchSettings.json

## Secret Pattern Hits
- Buffaly.OpenAI.ImageGeneration\OpenAIImageGenerationFacade.cs
- Buffaly.OpenAI.ImageGeneration\OpenAIImageGenerator.cs
- Buffaly.OpenAI.ImageGeneration.Smoke\Program.cs
- Buffaly.OpenAI.ImageGeneration.WebHarness\ImageGenerationHarnessJsonWsService.cs
- Buffaly.OpenAI.ImageGeneration.WebHarness\ImageHarnessRuntime.cs
- Buffaly.OpenAI.ImageGeneration.WebHarness\Program.cs
- Buffaly.OpenAI.ImageGeneration.WebHarness\ModulePackage\Skills\OpenAIImageGeneration\index.pts
- Buffaly.OpenAI.ImageGeneration.WebHarness\wwwroot\js\image-harness.js

## AttributionCheck
Before commit/push, run: powershell -NoProfile -ExecutionPolicy Bypass -File C:\\dev\\buffaly-ai\\scripts\\Test-PrePushAttribution.ps1 -RepoRoot <repo-root>

