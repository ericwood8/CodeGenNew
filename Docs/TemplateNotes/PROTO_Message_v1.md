# PROTO_Message_v1

`<Table>.proto` for gRPC users: the row as a message, a key message with the whole primary key (a composite key works), `List<Tables>Request` and `List<Tables>Response` (page number and size, items and a total), and a service with Get, List, Create, Update and Delete.

- **Types** (`ColumnTypes.Proto`): `int32` for int, smallint and tinyint, `int64`, `bool`, `double`, `float`, `bytes`, `google.protobuf.Timestamp` for a date or datetime, and `string` for text, a GUID, a time and a decimal or money value (protobuf has no decimal; the string keeps every digit). A nullable column is an `optional` field. Field names are `lower_snake_case`.
- **Numbering** follows column order, so a column added at the end keeps every existing number; reordering the table renumbers the fields, which breaks old clients.
- The package is the project name in lower case; `csharp_namespace` is `<ProjectName>.Grpc`. Implementing the service is up to you.
- Not in a plan unless the project names it in `PlanAlso`; the files go under `Protos`.
- **Checked** by compiling the files for two sample tables with `Grpc.Tools` (the real `protoc`) and creating a message and the generated client.
