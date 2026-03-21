FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Phylet.sln ./
COPY Phylet/Phylet.csproj Phylet/
COPY Phylet.Data/Phylet.Data.csproj Phylet.Data/
RUN dotnet restore Phylet/Phylet.csproj

COPY . .
RUN dotnet publish Phylet/Phylet.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5128
ENV Storage__MediaPath=/media
ENV Storage__DatabasePath=/data/phylet.db

EXPOSE 5128/tcp
EXPOSE 1900/udp
VOLUME ["/data"]

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "Phylet.dll"]
