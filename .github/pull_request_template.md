## What changed and why

## Checks
- [ ] `dotnet test CodeGenNew.Tests` passes (live database tests may report inconclusive)
- [ ] A template change has a test that renders it, and the samples still generate the same files (`codegen generate ... --dry-run`)
- [ ] No project, machine or person names, and no credentials, in any file (`LeftoverNamesTests` checks the common ones)
