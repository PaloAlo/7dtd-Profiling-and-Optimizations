# Copilot Instructions

## General Guidelines
- NEVER ADD PREFIXES TO LOGGING
- Avoid copying other modders' code; when using ideas, implement independently and provide explicit attribution and/or remove duplicated code on request.

## Project Guidelines
- Prefers session-level profiling: capture one profile per run/session (short snapshot around suspected bottleneck) instead of continuous full-process recording; focus on identifying when the bottleneck occurs and which methods are involved.