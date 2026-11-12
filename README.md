# Senior Test Engineer - Technical Test

## Overview

This is a comprehensive test automation framework demonstrating expertise across multiple testing layers:
- UI Automation (Playwright + C# + Reqnroll)
- API Automation
- Unit Testing
- Mock Testing (Moq)
- Component Testing
- Integration Testing
- Validation Testing

## Project Structure

```
TechnicalTest/
├── .github/
│   └── workflows/
│       ├── all-tests.yml
│       └── ui-tests.yml
├── performance/
│   ├── k6/
│   │   ├── journeys.json
│   │   ├── quote-journey.js
│   │   └── workload.json
│   ├── kubernetes/
│   │   └── k6-testrun.yaml
│   └── README.md
├── src/
│   ├── Api/
│   │   ├── Configuration/
│   │   │   └── PublicApiSettings.cs
│   │   ├── Clients/
│   │   │   ├── IPublicApiClient.cs
│   │   │   └── PublicApiClient.cs
│   │   ├── Models/
│   │   │   └── Post.cs
│   │   ├── Processors/
│   │   │   └── PostProcessor.cs
│   │   └── Validators/
│   │       └── PostValidator.cs
│   ├── Configuration/
│   │   └── ConfigurationHelper.cs
│   ├── Ui/
│   │   ├── Helpers/
│   │   │   ├── NetworkRecorder.cs
│   │   │   └── TestSettings.cs
│   │   └── Pages/
│   │       ├── Models/
│   │       │   ├── Quote.cs
│   │       │   └── Travellers.cs
│   │       ├── BasePage.cs
│   │       ├── InsurancePage.cs
│   │       ├── PlanOptionsPage.cs
│   │       └── TravellerDetailsPage.cs
│   └── TechnicalTest.csproj
│
├── tests/
│   ├── ApiTests/
│   │   ├── PublicApiClientOfflineTests.cs
│   │   ├── PublicApiContractTests.cs
│   │   └── PublicApiSettingsTests.cs
│   ├── ComponentTests/
│   │   └── PostProcessorComponentTests.cs
│   ├── Features/
│   │   ├── InsuranceBooking.feature
│   │   └── InsuranceBooking.feature.cs
│   ├── Hooks/
│   │   └── PlaywrightHooks.cs
│   ├── IntegrationTests/
│   │   └── PublicApiIntegrationTests.cs
│   ├── MockTests/
│   │   └── PostProcessorMockTests.cs
│   ├── StepDefinitions/
│   │   └── InsuranceBookingSteps.cs
│   ├── Support/
│   │   ├── JsonPlaceholderTestData.cs
│   │   └── UiContext.cs
│   ├── UnitTests/
│   │   └── PostValidatorTests.cs
│   ├── ValidationTests/
│   │   └── PostValidationTests.cs
│   ├── appsettings.json
│   ├── appsettings.{Dev,QA,Prod}.json
│   ├── AssemblyInfo.cs
│   ├── reqnroll.json
│   ├── TechnicalTest.Tests.csproj
│   └── test.runsettings
├── TechnicalTest.sln
└── README.md
```

##  Getting Started

### Prerequisites
- .NET 8.0 SDK
- PowerShell (for Playwright installation)
- Internet connection (for live API contract tests)

### Installation

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd "TechnicalTest"
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the solution**
   ```bash
   dotnet build
   ```

4. **Install Playwright browsers**
   ```powershell
   pwsh tests/bin/Debug/net8.0/playwright.ps1 install
   ```

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Tests by Category

**Unit Tests**
```bash
dotnet test --filter "Category=Unit"
```

**API Tests**
```bash
dotnet test --filter "Category=API"
```
This category contains offline HTTP-handler tests and live JSONPlaceholder contract tests, so the full category requires internet access.

**Mock Tests**
```bash
dotnet test --filter "Category=Mock"
```

**Component Tests**
```bash
dotnet test --filter "Category=Component"
```

**Integration Tests**
```bash
dotnet test --filter "Category=Integration"
```

**Validation Tests**
```bash
dotnet test --filter "Category=Validation"
```

**UI Tests**
```bash
dotnet test --filter "Category=UI"
```

### Run Tests on Specific Environment

**Dev Environment**
```bash
$env:TEST_ENV="Dev"
dotnet test --filter "Category=UI"
```

##  Expectation- Test Coverage

### 1. UI Test Automation (Playwright + Reqnroll)
-  Feature files with Scenario Outline and Examples
-  Page Object Model (POM) implementation
-  Network call capture
-  Dynamic element handling with waits
-  Form filling and validation
-  Multi-environment configuration
-  Parallel execution support
-  Comprehensive assertions

**Automate this Scenario Flow :**
- Navigate to a page https://insure.scti.co.nz/
- Pick One return journey
- Add destinations, travel dates, travellers and Get plan options
- Select One return jounery by hitting Select and customise plan
- Choose discount, excess, cancellation cover, and add extra cover as per your wish and hit Next: Traveller details
- complete Travellers Eligibilty and privacy declaration
- Fill Traveller form 
- Add Traveller, 1 Adult, 1 dependent, 1 non dependent

### 2. API Automation Tests
-  GET requests validation
-  POST requests validation
-  Schema validation
-  Status code validation
-  Positive and negative scenarios
-  Count validation
-  Invalid ID handling (404)

**API Endpoints:**
- `GET /posts` - Returns 100 posts with schema validation
- `GET /posts/{id}` - Returns specific post or null
- `POST /posts` - Creates new post and validates response

### 3. Unit Tests
-  PostValidator logic testing
-  Null checks
-  Field validations (UserId, Title, Body)
-  Edge cases
-  Boundary value testing
-  Multiple invalid fields handling

### 4. Mock Tests (Moq)
-  Simulating API success
-  Simulating API failures
-  Simulating empty lists
-  Simulating thrown exceptions
-  Simulating invalid posts
-  Verifying mock interactions

### 5. Component Tests
-  PostProcessor filtering logic
-  Business rules validation
-  Partial data handling
-  Mixed valid/invalid data scenarios
-  Integration of validator with processor

### 6. Integration Tests
-  Real HTTP client usage
-  Real API calls to jsonplaceholder.typicode.com
-  End-to-end flow validation
-  Data consistency checks
-  Multiple valid IDs testing

### 7. Validation Testing
-  Required fields validation
-  Title/Body constraints
-  UserId constraints
-  Boundary values
-  Special characters handling
-  Very long strings handling
-  Whitespace validation

## Configuration

### Environment-Specific Settings

**appsettings.json** (Default)
```json
{
  "BaseUrl": "https://insure.scti.co.nz/",
  "Timeout": 30000,
  "Headless": true
}
```
##  CI/CD Pipelines

### Add Multi-Environment UI Tests Pipeline
- Runs UI tests on Dev, QA, and Prod environments
- Manual trigger with environment selection
- Parallel execution across environments
- Test result artifacts upload

### Add All Tests Pipeline
- Runs all test categories in parallel
- Triggered on push/PR
- Separate jobs for each test type
- Individual test result uploads

## Technologies Used

- **Framework**: .NET 8.0
- **UI Automation**: Playwright 1.41.0
- **BDD Framework**: Reqnroll 2.0.3
- **Test Framework**: NUnit 4.0.1
- **Mocking**: Moq 4.20.70
- **Assertions**: FluentAssertions 6.12.0
- **Configuration**: Microsoft.Extensions.Configuration
- **API**: JSONPlaceholder (typicode.com)

##  Design Patterns

### Page Object Model (POM)
- `BasePage`: Abstract base with common functionality
- `InsurancePage`: Specific page implementation with locators and actions
- Separation of page structure from test logic

### Network Capture (UI)
- `NetworkRecorder` logs every response for the browser context (method, URL, status).
- The site's own API calls (`{BaseUrl}/api/**`) are also recorded with request and response bodies, so steps can assert on them.
- Trade-off: those bodies are captured through a Playwright route, because a response body can no longer be read once the page navigates (as it does straight after "Get a quote"). A routed call is sent by Playwright on the page's behalf and the result is handed back to the page, so it does not go through the browser's own network stack. A call that fails is reported to the page as failed and is never re-sent.

### Dependency Injection -API
- Interface-based design (IPublicApiClient)
- Easy mocking and testing
- Loose coupling

### Repository Pattern
- API client abstraction
- Centralized data access logic

## Key Features

### Implemented Features 
1. **Parallel Execution** - Tests can run concurrently
2. **Network Call Capture** - All HTTP requests logged
3. **Dynamic Wait Strategies** - Intelligent element waiting
4. **Multi-Environment Support** - Dev, QA, Prod configurations
5. **Comprehensive Assertions** - Text, attributes, elements
6. **Error Handling** - Graceful failure management
7. **Clean Architecture** - Separation of concerns
8. **Test Data Management** - Scenario outlines with examples
9. **CI/CD Ready** - GitHub Actions workflows
10. **Detailed Logging** - TRX test result reports

##  Test Execution Report

All tests are designed to:
-  Execute independently
-  Clean up resources properly
-  Provide clear failure messages
-  Support parallel execution
-  Be maintainable and scalable

## Contributing

This project should demonstrates enterprise-level test automation practices:
- Clean code principles
- SOLID principles
- DRY (Don't Repeat Yourself)
- Meaningful naming conventions
- Proper abstraction layers

## Support

For questions or issues, please refer to the technical assessment documentation.

---

**Author**: Senior Test Automation Engineer Candidate  
**Version**: 1.0.0  
**Last Updated**: 2024


--- 
## Non-Functional 

**Record below Journey**
**Automate this Scenario Flow :**
- Navigate to a page https://insure.scti.co.nz/
- Pick One return journey
- Add destinations, travel dates, travellers and Get plan options
- Select One return jounery by hitting Select and customise plan
- Choose discount, excess, cancellation cover, and add extra cover as per your wish and hit Next: Traveller details
- complete Travellers Eligibilty and privacy declaration
- Fill Traveller form 
- Add Traveller, 1 Adult, 1 dependent, 1 non dependent

**Create WLM model for baseline, load and stress test**
**Distributed WLM model**

