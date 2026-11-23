using CodeGenNew.Core.Catalog;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Core.Python;

/// <summary> <c>tests/test_catalog.py</c> of the Python API (PY_TestCatalog), beside the <c>conftest.py</c> the approval tests share: the cases add products and attributes of their own through the routes, then play the facet counts, the alternatives within an attribute,
/// the keyword over the attribute values, the ranges, paging and the refusals (<see cref="CatalogWords"/>). Each case asks for a keyword of its own, so the rows other cases left in the scratch database do not count. </summary>
public static class PythonCatalogTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && CatalogTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = CatalogTestPlan.For(database, project)!;
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_catalog\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        string numberSet = plan.NumberJson is null ? "" : $"            if number is not None:\n                row[{PythonNames.Str(plan.NumberJson)}] = number\n";
        string text = plan.NumberJson is null ? Text : Text + RangeText;
        var names = new (string, string)[]
        {
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut), ("§NUMBERSET", numberSet),
            ("§PRODUCTROUTE", PythonNames.Str(plan.ProductRoute)), ("§ATTRIBUTEROUTE", PythonNames.Str(plan.AttributeRoute)), ("§PRODUCTBODY", PythonNames.Str(plan.ProductBody)), ("§ATTRIBUTEBODY", PythonNames.Str(plan.AttributeBody)),
            ("§UNIQUE", "[" + string.Join(", ", plan.UniqueJson.Select(PythonNames.Str)) + "]"), ("§KEYJSON", PythonNames.Str(plan.KeyJson)), ("§TITLEJSON", PythonNames.Str(plan.TitleJson)), ("§PRODUCTCOLUMN", PythonNames.Str(plan.ProductColumnJson)),
            ("§NAMEJSON", PythonNames.Str(plan.NameJson)), ("§VALUEJSON", PythonNames.Str(plan.ValueJson)), ("§T_FILTER", CatalogWords.BadFilterType), ("§T_PAGE", CatalogWords.BadPageType),
        };
        return TemplateText.FillAll(text, names) + "\n";
    }

    private const string Text = """"
"""Catalog search with facets through the routes of the product table: the counts of each attribute value, the values of one attribute as alternatives, an attribute counted without its own filter, the keyword over the attribute values, paging and the refusals."""
import json
import uuid

import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
PRODUCTS = §PRODUCTROUTE
ATTRIBUTES = §ATTRIBUTEROUTE
PRODUCT = json.loads(§PRODUCTBODY)
ATTRIBUTE = json.loads(§ATTRIBUTEBODY)
UNIQUE = §UNIQUE


class Catalog:
    def __init__(self, client):
        self.client = client
        self.token = "zz" + uuid.uuid4().hex[:10]
        self.count = 0

    def product(self, text="", **attributes):
        """A product whose title starts with this case's token, with its attributes: a value, or a value and a number."""
        self.count += 1
        body = dict(PRODUCT)
        for name in UNIQUE:
            body[name] = f"{self.token}{self.count}"
        body[§TITLEJSON] = f"{self.token} {text}".strip()
        made = self.client.post(PRODUCTS, json=body)
        assert made.status_code == 201, made.text
        key = made.json()[§KEYJSON]
        for name, given in attributes.items():
            value, number = given if isinstance(given, tuple) else (given, None)
            row = dict(ATTRIBUTE)
            row[§PRODUCTCOLUMN] = key
            row[§NAMEJSON] = name
            row[§VALUEJSON] = value
§NUMBERSET            added = self.client.post(ATTRIBUTES, json=row)
            assert added.status_code == 201, added.text
        return key

    def ask(self, route, query=None, **more):
        params = {"q": query or self.token, **more}
        return self.client.get(f"{PRODUCTS}/{route}", params=params)

    def facets(self, query=None, **more):
        answer = self.ask("facets", query, **more)
        assert answer.status_code == 200, answer.text
        return answer.json()


def counts(answer):
    return {group["name"]: {item["value"]: item["count"] for item in group["values"]} for group in answer["facets"]}


@pytest.fixture
def catalog(application):
    """A client over a scratch database."""
    app, _ = application
§SIGNIN    yield Catalog(TestClient(app))
§SIGNOUT

def test_the_facets_count_the_products_that_carry_each_value_and_an_attribute_ignores_its_own_filter(catalog):
    catalog.product("a", Material="Copper", Color="Red")
    catalog.product("b", Material="Copper", Color="Blue")
    catalog.product("c", Material="Steel", Color="Red")
    everything = catalog.facets()
    assert everything["total"] == 3
    assert counts(everything)["Material"] == {"Copper": 2, "Steel": 1}
    chosen = catalog.facets(filter=["Material:Copper"])
    assert chosen["total"] == 2
    assert counts(chosen)["Material"] == {"Copper": 2, "Steel": 1}, "an attribute is counted without its own filter"
    assert counts(chosen)["Color"] == {"Red": 1, "Blue": 1}
    assert catalog.facets(filter=["Material:Copper", "Material:Steel"])["total"] == 3, "several values of one attribute are alternatives"
    assert catalog.facets(filter=["Material:Copper", "Color:Red"])["total"] == 1, "different attributes all have to match"
    missing = catalog.facets(filter=["Material:Copper", "Color:Green"])
    green = next(item for group in missing["facets"] if group["name"] == "Color" for item in group["values"] if item["value"] == "Green")
    assert missing["total"] == 0 and green == {"value": "Green", "count": 0, "selected": True}


def test_the_keyword_reads_the_attribute_values_in_any_case(catalog):
    word = "zq" + uuid.uuid4().hex[:8]
    catalog.product("plain", Finish=word)
    catalog.product("other", Finish="Matte")
    found = catalog.ask("narrow", word.upper()).json()
    assert found["total"] == 1, found
    assert catalog.ask("narrow", catalog.token).json()["total"] == 2


def test_narrow_pages_the_matching_products_in_key_order(catalog):
    keys = [catalog.product(str(n), Material="Copper") for n in range(3)]
    first = catalog.ask("narrow", pageSize=2).json()
    assert first["total"] == 3 and first["page"] == 1 and [item["id"] for item in first["items"]] == keys[:2], first
    second = catalog.ask("narrow", pageSize=2, page=2).json()
    assert [item["id"] for item in second["items"]] == keys[2:], second
    assert catalog.ask("narrow", pageSize=100000).json()["pageSize"] == 200


def test_a_bad_filter_and_a_bad_page_are_refused(catalog):
    bad = catalog.client.get(f"{PRODUCTS}/facets", params={"filter": "Material"})
    assert bad.status_code == 400 and bad.json()["type"].endswith("§T_FILTER"), bad.text
    page = catalog.client.get(f"{PRODUCTS}/narrow", params={"page": 0})
    assert page.status_code == 400 and page.json()["type"].endswith("§T_PAGE"), page.text
"""";

    private const string RangeText = """"


def test_a_range_keeps_the_products_with_a_number_inside_it_and_the_group_tells_its_ends(catalog):
    catalog.product("a", Voltage=("120 V", 120))
    catalog.product("b", Voltage=("240 V", 240))
    catalog.product("c", Voltage=("600 V", 600))
    inside = catalog.facets(range=["Voltage:100:300"])
    voltage = next(group for group in inside["facets"] if group["name"] == "Voltage")
    assert inside["total"] == 2
    assert float(voltage["min"]) == 120 and float(voltage["max"]) == 600, "the ends are those of the attribute without its own range"
    assert float(voltage["selectedMin"]) == 100 and float(voltage["selectedMax"]) == 300
    assert catalog.facets(range=["Voltage::200"])["total"] == 1
    assert catalog.facets(range=["Voltage:500:"])["total"] == 1
    assert catalog.facets(range=["Voltage:1000:2000"])["total"] == 0


def test_a_range_without_numbers_is_refused(catalog):
    for value in ("Voltage::", "Voltage:abc:5", "Voltage:100"):
        bad = catalog.client.get(f"{PRODUCTS}/narrow", params={"range": value})
        assert bad.status_code == 400 and bad.json()["type"].endswith("§T_FILTER"), bad.text
"""";
}
