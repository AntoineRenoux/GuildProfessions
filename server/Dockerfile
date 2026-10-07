FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GuildProfessions.Server.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
VOLUME /data
ENV ConnectionStrings__Default="Data Source=/data/guildprofessions.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "GuildProfessions.Server.dll"]
