using Hound.Grocery.Services;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class StateFileServiceTests
{
    [TestMethod]
    public void FileName_MapsEveryStateFile_ToExpectedMarkdown()
    {
        Assert.AreEqual("shopping-list.md", StateFileService.FileName(GroceryStateFile.ShoppingList));
        Assert.AreEqual("preferences.md", StateFileService.FileName(GroceryStateFile.Preferences));
        Assert.AreEqual("budget-ledger.md", StateFileService.FileName(GroceryStateFile.BudgetLedger));
        Assert.AreEqual("basket-trace.md", StateFileService.FileName(GroceryStateFile.BasketTrace));
        Assert.AreEqual("purchase-history.md", StateFileService.FileName(GroceryStateFile.PurchaseHistory));
    }
}
