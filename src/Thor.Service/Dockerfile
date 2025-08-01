﻿FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER root
RUN ln -sf /usr/share/zoneinfo/Asia/Shanghai /etc/localtime
RUN echo "Asia/Shanghai" > /etc/timezone
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM --platform=$BUILDPLATFORM node:20.18-alpine3.21 AS lobe
WORKDIR /src

# 设置npm镜像源以加速ARM64构建
RUN npm config set registry https://registry.npmmirror.com
RUN yarn config set registry https://registry.npmmirror.com

# 复制package文件优先，利用缓存
COPY lobe/package*.json lobe/yarn.lock* ./
RUN yarn install --frozen-lockfile --network-timeout 100000 || npm install --force

# 复制源代码并构建
COPY lobe .
RUN yarn run build || npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Thor.Service/Thor.Service.csproj", "src/Thor.Service/"]
COPY ["src/extensions/Thor.AWSClaude/Thor.AWSClaude.csproj", "src/extensions/Thor.AWSClaude/"]
COPY ["src/Thor.Abstractions/Thor.Abstractions.csproj", "src/Thor.Abstractions/"]
COPY ["src/Thor.Domain.Shared/Thor.Domain.Shared.csproj", "src/Thor.Domain.Shared/"]
COPY ["src/extensions/Thor.AzureOpenAI/Thor.AzureOpenAI.csproj", "src/extensions/Thor.AzureOpenAI/"]
COPY ["src/extensions/Thor.Claude/Thor.Claude.csproj", "src/extensions/Thor.Claude/"]
COPY ["src/extensions/Thor.CustomOpenAI/Thor.CustomOpenAI.csproj", "src/extensions/Thor.CustomOpenAI/"]
COPY ["src/extensions/Thor.DeepSeek/Thor.DeepSeek.csproj", "src/extensions/Thor.DeepSeek/"]
COPY ["src/extensions/Thor.ErnieBot/Thor.ErnieBot.csproj", "src/extensions/Thor.ErnieBot/"]
COPY ["src/extensions/Thor.GCPClaude/Thor.GCPClaude.csproj", "src/extensions/Thor.GCPClaude/"]
COPY ["src/extensions/Thor.Gemini/Thor.Gemini.csproj", "src/extensions/Thor.Gemini/"]
COPY ["src/extensions/Thor.GiteeAI/Thor.GiteeAI.csproj", "src/extensions/Thor.GiteeAI/"]
COPY ["src/extensions/Thor.Hunyuan/Thor.Hunyuan.csproj", "src/extensions/Thor.Hunyuan/"]
COPY ["src/extensions/Thor.MetaGLM/Thor.MetaGLM.csproj", "src/extensions/Thor.MetaGLM/"]
COPY ["src/extensions/Thor.MiniMax/Thor.MiniMax.csproj", "src/extensions/Thor.MiniMax/"]
COPY ["src/extensions/Thor.Moonshot/Thor.Moonshot.csproj", "src/extensions/Thor.Moonshot/"]
COPY ["src/extensions/Thor.Ollama/Thor.Ollama.csproj", "src/extensions/Thor.Ollama/"]
COPY ["src/extensions/Thor.OpenAI/Thor.OpenAI.csproj", "src/extensions/Thor.OpenAI/"]
COPY ["src/extensions/Thor.Qiansail/Thor.Qiansail.csproj", "src/extensions/Thor.Qiansail/"]
COPY ["src/extensions/Thor.SiliconFlow/Thor.SiliconFlow.csproj", "src/extensions/Thor.SiliconFlow/"]
COPY ["src/extensions/Thor.SparkDesk/Thor.SparkDesk.csproj", "src/extensions/Thor.SparkDesk/"]
COPY ["src/extensions/Thor.VolCenGine/Thor.VolCenGine.csproj", "src/extensions/Thor.VolCenGine/"]
COPY ["src/framework/Thor.BuildingBlocks.Cache/Thor.BuildingBlocks.Cache.csproj", "src/framework/Thor.BuildingBlocks.Cache/"]
COPY ["src/framework/Thor.BuildingBlocks.Event/Thor.BuildingBlocks.Event.csproj", "src/framework/Thor.BuildingBlocks.Event/"]
COPY ["src/framework/Thor.LocalEvent/Thor.LocalEvent.csproj", "src/framework/Thor.LocalEvent/"]
COPY ["src/framework/Thor.LocalMemory.Cache/Thor.LocalMemory.Cache.csproj", "src/framework/Thor.LocalMemory.Cache/"]
COPY ["src/framework/Thor.RabbitMQEvent/Thor.RabbitMQEvent.csproj", "src/framework/Thor.RabbitMQEvent/"]
COPY ["src/framework/Thor.Stack.Rabbit/Thor.Rabbit.csproj", "src/framework/Thor.Stack.Rabbit/"]
COPY ["src/framework/Thor.RedisMemory.Cache/Thor.RedisMemory.Cache.csproj", "src/framework/Thor.RedisMemory.Cache/"]
COPY ["src/Provider/Thor.Provider.DM/Thor.Provider.DM.csproj", "src/Provider/Thor.Provider.DM/"]
COPY ["src/Thor.Core/Thor.Core.csproj", "src/Thor.Core/"]
COPY ["src/Thor.Domain/Thor.Domain.csproj", "src/Thor.Domain/"]
COPY ["src/Thor.Infrastructure/Thor.Infrastructure.csproj", "src/Thor.Infrastructure/"]
COPY ["src/Provider/Thor.Provider.MySql/Thor.Provider.MySql.csproj", "src/Provider/Thor.Provider.MySql/"]
COPY ["src/Provider/Thor.Provider.PostgreSQL/Thor.Provider.PostgreSQL.csproj", "src/Provider/Thor.Provider.PostgreSQL/"]
COPY ["src/Provider/Thor.Provider.Sqlite/Thor.Provider.Sqlite.csproj", "src/Provider/Thor.Provider.Sqlite/"]
COPY ["src/Provider/Thor.Provider.SqlServer/Thor.Provider.SqlServer.csproj", "src/Provider/Thor.Provider.SqlServer/"]
COPY ["src/Thor.ServiceDefaults/Thor.ServiceDefaults.csproj", "src/Thor.ServiceDefaults/"]
COPY ["src/extensions/Thor.AzureDatabricks/Thor.AzureDatabricks.csproj", "src/extensions/Thor.AzureDatabricks/"]
RUN dotnet restore "src/Thor.Service/Thor.Service.csproj"
COPY . .
WORKDIR "/src/src/Thor.Service"
RUN dotnet build "./Thor.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Thor.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=lobe /src/dist ./wwwroot
ENTRYPOINT ["dotnet", "Thor.Service.dll"]
