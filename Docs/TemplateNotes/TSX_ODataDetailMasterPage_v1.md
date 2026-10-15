# TSX_ODataDetailMasterPage_v1

The OData form of the React master-detail page (`TSX_DetailMasterPage` run with `Variant=OData`): the same page as `TSX_ODataPage` plus one read-only grid per child table under the form. A child whose table is served as OData too is read with `GET /odata/<child>?$filter=<fk> eq <parent key>&$orderby=<key>` (following the next links) instead of the whole table being read and filtered in the browser; any other child uses its REST route as before. A child row's Edit and Delete still go to the child's own page and REST route; the way back leads to the OData page the person came from (or to the replacing page with `ODataScreens=Replace`).

Written only for the master-detail tables the project serves as OData. A table with `EditableChildren` is not served. Notes of the page: `TSX_ODataPage_v1.md`.
