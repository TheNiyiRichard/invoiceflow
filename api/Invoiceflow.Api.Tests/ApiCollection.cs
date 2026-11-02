namespace Invoiceflow.Api.Tests;

// All test classes share ONE ApiFactory (one database reset, one in-memory API)
// and run one after another, so they can't race on the reset.
[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory> { }