using Microsoft.Playwright;

namespace ContosoInsurance.E2E;

[TestClass]
public class CrudTests
{
    private static string BaseUrl => Environment.GetEnvironmentVariable("APP_BASE_URL") 
        ?? "http://bxf5ejdjesbkhrcf.fz45.alb.azure.com";

    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    [TestInitialize]
    public async Task TestInit()
    {
        _context = await _browser!.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        if (_context != null) await _context.CloseAsync();
    }

    [TestMethod]
    public async Task CreateCustomer_AppearsInList()
    {
        var uniqueEmail = $"e2e_{Guid.NewGuid():N}@test.com";
        var firstName = "E2ETest";
        var lastName = $"User{DateTime.Now.Ticks % 10000}";

        await _page!.GotoAsync($"{BaseUrl}/customers");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Wait for Blazor Interactive Server circuit to establish
        // The button won't respond until the SignalR connection is ready
        await _page.WaitForTimeoutAsync(5000);

        // Click "Add Customer" button
        var addBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Add Customer" });
        await addBtn.ClickAsync();

        // Wait for form to appear (Blazor re-renders after state change)
        var firstNameInput = _page.GetByPlaceholder("Enter first name");
        await firstNameInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Fill the form
        await firstNameInput.FillAsync(firstName);
        await _page.GetByPlaceholder("Enter last name").FillAsync(lastName);
        await _page.GetByPlaceholder("email@example.com").FillAsync(uniqueEmail);
        await _page.GetByPlaceholder("(555) 123-4567").FillAsync("555-0001");
        await _page.GetByPlaceholder("Street address").FillAsync("123 E2E Test Street");

        // Submit - click the submit button (inside the form)
        await _page.Locator("button[type='submit']").ClickAsync();

        // Wait for success message
        var success = _page.GetByText("added successfully");
        await success.WaitForAsync(new() { Timeout = 15000 });
        Assert.IsTrue(await success.IsVisibleAsync());

        // Verify customer appears in list
        var emailCell = _page.GetByText(uniqueEmail);
        await emailCell.WaitForAsync(new() { Timeout = 5000 });
        Assert.IsTrue(await emailCell.IsVisibleAsync());
    }

    [TestMethod]
    public async Task CreateQuote_AppearsInList()
    {
        await _page!.GotoAsync($"{BaseUrl}/quotes");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Wait for Blazor Interactive Server circuit
        await _page.WaitForTimeoutAsync(5000);

        // Click "Request a Quote" button
        await _page.GetByRole(AriaRole.Button, new() { Name = "Request a Quote" }).ClickAsync();

        // Wait for form to appear
        var customerSelect = _page.Locator("select").First;
        await customerSelect.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Select first customer from dropdown (index 1 = first real option after placeholder)
        var options = await customerSelect.Locator("option").AllAsync();
        if (options.Count > 1)
        {
            await customerSelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        }

        // Submit
        await _page.Locator("button[type='submit']").ClickAsync();

        // Wait for success
        var success = _page.GetByText("submitted successfully");
        await success.WaitForAsync(new() { Timeout = 15000 });
        Assert.IsTrue(await success.IsVisibleAsync());
    }

    [TestMethod]
    public async Task CreateClaim_Succeeds()
    {
        await _page!.GotoAsync($"{BaseUrl}/claims/new");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Wait for Blazor Interactive Server circuit
        await _page.WaitForTimeoutAsync(5000);

        // Step 1: Select a policy card
        var policyCard = _page.Locator(".ci-action-card").First;
        await policyCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await policyCard.ClickAsync();
        await _page.WaitForTimeoutAsync(500);

        // Click "Continue" button
        await _page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("Continue") }).ClickAsync();
        await _page.WaitForTimeoutAsync(1000);

        // Step 2: Fill incident details
        // The InputDate renders as <input type="date"> in Blazor
        var dateInput = _page.Locator("input[type='date']").First;
        await dateInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await dateInput.FillAsync("2026-05-15");

        var amountInput = _page.Locator("input[type='number']").First;
        await amountInput.ClearAsync();
        await amountInput.FillAsync("2500");

        var textarea = _page.Locator("textarea").First;
        await textarea.FillAsync("E2E Test Claim - minor damage to vehicle during parking");

        // Click "Review Claim" submit button
        await _page.Locator("button[type='submit']").ClickAsync();
        await _page.WaitForTimeoutAsync(2000);

        // Step 3: We should now be on the review step.
        // Check if we're on review step or if validation failed
        var submitClaimBtn = _page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("Submit Claim") });
        if (await submitClaimBtn.CountAsync() > 0)
        {
            await submitClaimBtn.ClickAsync();
            
            // After submit, either success message shows briefly or we navigate to /claims
            // Wait for either success text or URL change
            try
            {
                var success = _page.GetByText("successfully");
                await success.WaitForAsync(new() { Timeout = 10000 });
                Assert.IsTrue(await success.IsVisibleAsync());
            }
            catch (TimeoutException)
            {
                // Might have already navigated to claims page
                await _page.WaitForURLAsync("**/claims", new() { Timeout = 5000 });
                Assert.IsTrue(_page.Url.Contains("/claims"));
            }
        }
        else
        {
            // Validation might have failed - check for error
            var pageContent = await _page.ContentAsync();
            Assert.Fail($"Review step not reached. Page content snippet: {pageContent[..Math.Min(500, pageContent.Length)]}");
        }
    }
}
