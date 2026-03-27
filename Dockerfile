FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG TARGETOS
ARG TARGETARCH

COPY Phylet.sln ./
COPY Phylet/Phylet.csproj Phylet/
COPY Phylet.Data/Phylet.Data.csproj Phylet.Data/
RUN case "$TARGETARCH" in \
      "amd64") export DOTNET_ARCH="x64" ;; \
      "arm64") export DOTNET_ARCH="arm64" ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet restore Phylet/Phylet.csproj -r "${TARGETOS}-${DOTNET_ARCH}"

COPY . .
RUN case "$TARGETARCH" in \
      "amd64") export DOTNET_ARCH="x64" ;; \
      "arm64") export DOTNET_ARCH="arm64" ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet publish Phylet/Phylet.csproj -c Release -o /app/publish --no-restore -r "${TARGETOS}-${DOTNET_ARCH}"

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
