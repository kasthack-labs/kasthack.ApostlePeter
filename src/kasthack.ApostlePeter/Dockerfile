#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY [".editorconfig", "kasthack.ApostlePeter/"]
COPY ["Directory.Build.props", "kasthack.ApostlePeter/"]
COPY ["kasthack.ApostlePeter/kasthack.ApostlePeter.csproj", "kasthack.ApostlePeter/"]
RUN dotnet restore "kasthack.ApostlePeter/kasthack.ApostlePeter.csproj"
COPY . .
WORKDIR "/src/kasthack.ApostlePeter"
RUN dotnet build "kasthack.ApostlePeter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "kasthack.ApostlePeter.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "kasthack.ApostlePeter.dll"]