# Deployment scripts via ssh

## Usage

Make sure to drop valid ssh-credentials in "./secrets.json":

```json
{
    "username": "username",
    "password": "password",
}
```

Then you can run the deployment script via dotnet CLI:

```bash
dotnet run --project "Shush/Shush.csproj" --tag v0.6.7 --repository-path "C:/git/Aind.Behavior.VrForaging"
```

The folder structure inside `./FilesToTransfer` will be mirrored on the target machine.