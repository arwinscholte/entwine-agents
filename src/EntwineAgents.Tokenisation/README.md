# EntwineAgents.Tokenisation

Tokenisation at rest — pseudonymisation with a separated key (GDPR Art. 4(5)).

Store **deterministic, cross-run-stable tokens** in your data; keep real values only as AES-GCM ciphertext
in a separate, tenant-scoped key store. The same identity always becomes the same token, so saved datasets
converge; nothing readable exists outside an authorised reveal.

```csharp
var keys = new ReidentificationKeyStore(keyStoreDb, new AesValueProtector(key32Bytes));

var tokens = await keys.RegisterAsync("tenant-a", [("Account", "Acme Corp")]);
// tokens["Acme Corp"] -> "ACCOUNT_1c50543a9d6b"  (stable across runs)

var real = await keys.RevealAsync("tenant-a", [token]);   // authorised read; wrong tenant reveals nothing
```

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET.
