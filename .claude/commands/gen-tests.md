Generate unit tests with 100% code coverage for the class named: $ARGUMENTS

## Steps

1. **Locate the class** — search the codebase for a file containing `class $ARGUMENTS` or `record $ARGUMENTS`. Read the full source file.

2. **Discover the test project** — look under `tests/` for a `*.Tests.csproj`. If none exists for the source project, note that a new test project must be created. Check any existing test files for:
   - Test framework (`xUnit`, `NUnit`, `MSTest`)
   - Assertion library (`FluentAssertions`, `Shouldly`, built-in `Assert`)
   - Mocking library (`Moq`, `NSubstitute`)
   - Naming convention for test methods (e.g. `MethodName_Scenario_ExpectedResult`)

3. **Analyze `$ARGUMENTS` for 100% coverage** — for every public and internal member:
   - All branches: every `if`, `else`, `switch` arm, `?:`, `??`, `?.`
   - All return paths (early returns, guard clauses)
   - Exception paths: what throws and when
   - Null / empty / boundary inputs
   - Async/await paths (cancellation, faulted tasks)
   - Any events raised

4. **Generate the test class**:
   - Place it in the test project mirroring the source namespace (e.g. `GeoAssets.Core.Tests.Services`)
   - File name: `$ArgumentsTests.cs`
   - One `[Fact]` / `[Test]` per scenario
   - Arrange / Act / Assert structure (no comments unless logic is non-obvious)
   - Mock all external dependencies — never hit real I/O, databases, or network
   - Use `[Theory]` / `[TestCase]` for parameterized boundary cases
   - Each test must be independent and not rely on test execution order

5. **Coverage checklist** — after writing tests, list every method and confirm which branches are covered. Flag any branch that cannot be reached (dead code).

6. **If the test project does not exist**, output:
   - The `.csproj` file content (targeting the same `<TargetFramework>` as the source project, with xUnit + Moq unless the project already uses others)
   - Instructions to add it to the solution: `dotnet sln add tests/<ProjectName>.Tests/<ProjectName>.Tests.csproj`

Do not add XML doc comments. Do not add using directives for namespaces that are not needed. Follow the naming and style conventions already present in the test project; if none exist, use xUnit + FluentAssertions + NSubstitute.
