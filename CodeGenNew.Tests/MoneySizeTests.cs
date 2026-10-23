using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A decimal the size SQL Server gives money, and the project's lists of money columns. </summary>
[TestClass]
public class MoneySizeTests
{
    [TestMethod]
    [DataRow(19, 4, true)]
    [DataRow(10, 4, true)]
    [DataRow(19, 2, false)]
    [DataRow(18, 4, false)]
    [DataRow(38, 4, false)]
    [DataRow(8, 2, false)]
    public void Money_is_the_size_of_the_money_and_smallmoney_types(int precision, int scale, bool expected) =>
        Assert.AreEqual(expected, NumericClassifier.IsMoneySize(precision, scale));

    [TestMethod]
    [DataRow("DiscountPercent", true)]
    [DataRow("ItemWeight", true)]
    [DataRow("QuantityUmRatio", true)]
    [DataRow("UnitPrice", false)]
    [DataRow("Charge", false)]
    [DataRow("Surplus", false)]
    public void A_name_that_counts_or_measures_something_is_not_money(string name, bool expected) =>
        Assert.AreEqual(expected, NumericClassifier.IsNotMoneyName(name));

    [TestMethod]
    public void The_name_rule_is_as_it_was()
    {
        Assert.IsFalse(NumericClassifier.IsCurrencyName("CreditLimit"));
        Assert.IsTrue(NumericClassifier.IsCurrencyName("OrderTotal"));
        Assert.IsTrue(NumericClassifier.IsCurrencyName("UnitPrice"));
        Assert.IsFalse(NumericClassifier.IsCurrencyName("QuantityUmRatio"));
        Assert.IsFalse(NumericClassifier.IsCurrencyName("DiscountPercent"));
    }

    [TestMethod]
    public void The_money_lists_are_settings_with_hints()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("MoneyColumns", "Surplus, Invoice.Extra"), new("NotMoneyColumns", "Cost")]);

        CollectionAssert.AreEqual(new[] { "Surplus", "Invoice.Extra" }, project.MoneyColumns);
        CollectionAssert.AreEqual(new[] { "Cost" }, project.NotMoneyColumns);
        foreach (string key in new[] { "MoneyColumns", "NotMoneyColumns" })
        {
            Assert.Contains(key, ProjectSettings.Keys);
            Assert.IsTrue(ProjectSettingsHints.All.ContainsKey(key), key);
        }
    }
}
