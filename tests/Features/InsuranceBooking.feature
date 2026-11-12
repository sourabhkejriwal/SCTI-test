@UI
Feature: Insurance Booking Journey
  As a traveller leaving from New Zealand
  I want to quote and customise a one return journey policy and enter everyone's details
  So that I can apply for cover for my whole family

  The journey stops on the Traveller details page: nothing is submitted beyond it and nothing is purchased.
  Travel dates are relative to today (in the site's time zone) so the scenario never goes stale.
  Dates of birth are derived from the ages so they always match the quote.

  Interpretation of "Add Traveller, 1 Adult, 1 dependent, 1 non dependent": the party is one adult (the main
  policyholder), one dependent child and one non-dependent child. The site takes the party on the quote form
  and then renders one traveller block per person on the Traveller details page, where each one is completed.

  Scenario Outline: Quote, customise and add travellers for a one return journey
    Given I am on the SCTI travel insurance home page
    When I choose the "One return journey" cover
    And I add the destinations "<destinations>"
    And I add travel dates departing in 30 days for a 14 day trip
    And I add 1 adult aged <adult age>, 1 dependent child aged <dependent age> and 1 non-dependent child aged <non-dependent age>
    And I get plan options
    Then plan options should be quoted for destination codes "<destination codes>"

    When I select and customise the one return journey plan
    And I choose discount "<discount>", excess "<excess>", cancellation cover "<cancellation cover>" and extra cover "<extra cover>"
    Then the selected plan premium should reflect my customisation

    When I continue to traveller details
    Then the traveller details page should show the selected plan premium

    When I confirm all travellers are eligible NZ residents who have not started their journey and have no fraud history
    And I accept the privacy declaration
    Then the traveller form should list the main policyholder, the dependent child and the non-dependent child

    When I enter the main policyholder details
      | Field              | Value                                               |
      | Title              | Mr.                                                 |
      | First name         | Test                                                |
      | Last name          | Policyholder                                        |
      | Mobile phone       | 0211234567                                          |
      | Email address      | test.policyholder@example.com                       |
      | Street address     | 1 Queen Street                                      |
      | Address suggestion | 1 Queen Street, Auckland CBD, Auckland, New Zealand |
      | Suburb             | Auckland CBD                                        |
      | City               | Auckland                                            |
      | Postcode           | 1010                                                |
    And I enter the dependent child details with first name "Test" and last name "Dependent"
    And I enter the non-dependent child details with first name "Test" and last name "Nondependent"
    Then all travellers should be completed without validation errors
    And no API call in the journey should have failed

    Examples:
      | destinations    | destination codes | adult age | dependent age | non-dependent age | discount | excess | cancellation cover | extra cover |
      | Australia, Fiji | AUS, FJI          | 40        | 10            | 12                | No       | $100   | $5,000             | Cruise      |

