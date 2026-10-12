# Проект “Фото проводник”
## Жарков Сергей Иванович 231-322
## Запуск

Требуются Windows и .NET SDK 10.
Выполнить из корневой папки проекта:

```powershell
dotnet run --project .\src\PhotoExplorer.Desktop\PhotoExplorer.Desktop.csproj
```

Запуск с демонстрационными изображениями:

```powershell
dotnet run --project .\src\PhotoExplorer.Desktop\PhotoExplorer.Desktop.csproj -- --demo
```

## Сборка версии для показа

```powershell
dotnet publish .\src\PhotoExplorer.Desktop\PhotoExplorer.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\publish
```
