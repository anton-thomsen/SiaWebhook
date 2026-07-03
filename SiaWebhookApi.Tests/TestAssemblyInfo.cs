// Several integration tests configure process-global environment variables
// (SIA_JWT_SECRET, ZEPCAM_API_KEY, ...) before booting the app. Env vars are
// shared across the whole test process, so classes must not run concurrently.
// The suite is small enough that serializing it costs nothing.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
