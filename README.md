# Tiskové balíčky

Windows WPF aplikace pro ukládání a opakované odesílání PDF se zachovaným nastavením PCL6/PCL XL ovladače. Podporuje například OKI C844 a Canon i-SENSYS X 1533P II.

## Spuštění

Na Windows 10/11 s .NET 8 SDK a nainstalovaným ovladačem **PCL6/PCL XL** pro danou tiskárnu (OKI C844 nebo Canon i-SENSYS X 1533P II):

```powershell
dotnet restore
dotnet test
dotnet run --project src/PrintPackages
```

Balíček `.printpkg` je ZIP obsahující PDF, `manifest.json`, `driver.devmode` a náhled první stránky. Při tisku aplikace vyžaduje shodný název i verzi ovladače, aby nezměnila výsledek tisku.

## Hotová Windows aplikace

Po označení verze ve formátu `v1.0.0` GitHub Actions vytvoří v **Releases** soubor `PrintPackages-win-x64.zip`. Ten se na Windows pouze rozbalí a spustí se `PrintPackages.exe`; .NET Runtime není potřeba instalovat. Cílová tiskárna s PCL6/PCL XL ovladačem však musí být v systému nainstalovaná.
