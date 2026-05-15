Generate unit tests with 100% code coverage for a single method. Argument format: `ClassName/MethodName` or `ClassName.MethodName`.

Parse `$ARGUMENTS` to extract:
- **ClassName** — the part before `/` or `.`
- **MethodName** — the part after `/` or `.`

## Steps

1. **Locate the class** — search the codebase for `class ClassName` or `record ClassName`. Read the full source file to understand context, fields, constructor dependencies, and all overloads of `MethodName`.

2. **Discover the test project** — look under `tests/` for a `*.Tests` project matching the source project. Check existing test files for:
   - Test framework (`xUnit`, `NUnit`, `MSTest`)
   - Assertion library (`FluentAssertions`, `Shouldly`, built-in `Assert`)
   - Mocking library (`Moq`, `NSubstitute`)
   - Naming convention (e.g. `MethodName_Scenario_ExpectedResult`)

3. **Analyze `MethodName` exhaustively for 100% coverage**:
   - Trace every execution path from entry to exit
   - Every `if` / `else` branch (including negated conditions)
   - Every `switch` / `switch` expression arm (including `default` / `_`)
   - Null-coalescing (`??`, `?.`, `??=`) — both null and non-null sides
   - Guard clauses and early returns
   - Every exception that can be thrown (explicit `throw` and uncaught propagation)
   - Loop boundaries: empty collection, single element, multiple elements
   - Async: happy path, cancellation via `CancellationToken`, faulted `Task`
   - If the method has overloads, cover all of them

4. **Check whether a test class for `ClassName` already exists**:
   - **Yes** → output only the new `[Fact]` / `[Test]` / `[Theory]` methods to add, with a comment showing where to insert them
   - **No** → output the full test class scaffolding including the new methods

5. **Write the tests**:
   - One test per scenario; name: `MethodName_Condition_ExpectedResult`
   - Arrange / Act / Assert structure
   - Mock all external dependencies — never hit real I/O, databases, or network
   - Use `[Theory]` / `[TestCase]` for parameterized boundary cases
   - Tests must be independent and order-agnostic

6. **Coverage summary** — after writing all tests, list every branch of `MethodName` and confirm each is covered. Flag any branch that is unreachable (dead code).

7. **If the test project does not exist**, output:
   - The `.csproj` file content (same `<TargetFramework>` as source, xUnit + NSubstitute + FluentAssertions unless project already uses others)
   - Instructions: `dotnet sln add tests/<ProjectName>.Tests/<ProjectName>.Tests.csproj`

Do not add XML doc comments. Do not add unused `using` directives. Follow conventions found in the existing test project; if none exist, use xUnit + FluentAssertions + NSubstitute.
