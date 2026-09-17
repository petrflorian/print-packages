# Tiskové balíčky

Windows WPF aplikace pro ukládání a opakované odesílání PDF na OKI C844 se zachovaným nastavením ovladače.

## Spuštění

Na Windows 10/11 s .NET 8 SDK a nainstalovaným ovladačem **OKI PCL6/PCL XL** pro C844:

```powershell
dotnet restore
dotnet test
dotnet run --project src/PrintPackages
```

Balíček `.printpkg` je ZIP obsahující PDF, `manifest.json`, `driver.devmode` a náhled první stránky. Při tisku aplikace vyžaduje shodný název i verzi ovladače, aby nezměnila výsledek tisku.

## Hotová Windows aplikace

Po označení verze ve formátu `v1.0.0` GitHub Actions vytvoří v **Releases** soubor `PrintPackages-win-x64.zip`. Ten se na Windows pouze rozbalí a spustí se `PrintPackages.exe`; .NET Runtime není potřeba instalovat. Tiskárna OKI C844 s ovladačem PCL6/PCL XL však musí být v systému nainstalovaná.
