# CS_SerializationDtos_v1

Three serialization contracts of the same data class in one file; keep the one that suits and delete the others.

- `<Table>Basic`: plain `[Serializable]`.
- `<Table>Serializable`: `ISerializable`; `GetObjectData` writes every column by name, the protected constructor reads them back.
- `<Table>Xml`: `XmlSerializer` attributes: key columns as XML attributes, other columns elements, computed columns `[XmlIgnore]`; the root is the table name.
- Checked: the `ISerializable` class and the XML class round-trip.
