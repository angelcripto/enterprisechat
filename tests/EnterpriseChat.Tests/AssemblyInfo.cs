// WebApplicationFactory<TEntryPoint> doesn't play well with parallel test
// classes initialising the same entry point simultaneously (see aspnetcore
// issue 27316). Forcing tests to run one collection at a time avoids the
// "entry point exited without ever building an IHost" flake.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
