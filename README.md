# Azure Function + Key Vault 项目 (使用托管标识)

这是一个完整的 Azure 云原生项目，演示如何使用 Azure Functions 从 Key Vault 安全地读取机密信息。该项目采用托管标识 (Managed Identity) 进行身份验证，基于 RBAC 授权，使用 Bicep 定义基础设施即代码 (Infrastructure as Code)，并通过 GitHub Actions 和 OIDC 实现自动化部署。

## 项目概述

本项目实现了一个基于 .NET 9 的 Azure Function (隔离工作进程模型)，该函数通过系统分配的托管标识从 Azure Key Vault 读取机密。整个基础设施使用 Bicep 定义，支持通过 GitHub Actions 使用 OpenID Connect (OIDC) 进行无密钥部署。

核心功能:
- HTTP 触发的 Azure Function，接受 `secretName` 查询参数
- 使用 `DefaultAzureCredential` 通过托管标识进行身份验证
- 基于 RBAC 的 Key Vault 访问控制（非访问策略）
- 完全自动化的 CI/CD 流水线

## 架构

### 组件架构

```
┌─────────────────┐         ┌──────────────────┐
│  GitHub Actions │────────>│  Azure Function  │
│  (OIDC 部署)    │         │  (Consumption)   │
└─────────────────┘         └──────────────────┘
                                     │
                                     │ System-Assigned
                                     │ Managed Identity
                                     │
                            ┌────────▼──────────┐
                            │  Azure Key Vault  │
                            │  (RBAC 授权)      │
                            └───────────────────┘
                                     │
                         授予角色: Key Vault Secrets User
```

### Azure 资源

| 资源类型 | 资源名称 | 说明 |
|---------|---------|------|
| **资源组** | `rg-kvfuncapp-cc` | 所有资源的容器 |
| **存储账户** | `stkvfuncapp[uniquestring]` | Azure Functions 运行时所需（存储函数代码、日志等） |
| **应用服务计划** | `asp-kvfuncapp` | 消费型计划 (Y1 Dynamic)，按需付费 |
| **函数应用** | `func-kvfuncapp` | .NET 9 隔离工作进程，启用系统分配的托管标识 |
| **Key Vault** | `kv-kvfuncapp` | 启用 RBAC 授权，软删除保留期 7 天 |
| **角色分配** | (自动生成) | 授予 Function App 的托管标识 "Key Vault Secrets User" 角色 |
| **Azure AD 应用注册** | `github-deploy` | 用于 GitHub Actions OIDC 身份验证，带有联合凭据 |

### 关键设计决策

1. **托管标识优于服务主体**: 使用系统分配的托管标识消除了管理应用程序密钥的需求
2. **RBAC 授权优于访问策略**: Key Vault 使用 Azure RBAC 而不是传统的访问策略，实现统一的权限管理
3. **基础设施即代码**: 所有资源使用 Bicep 模块化定义，可版本控制且可重复部署
4. **OIDC 无密钥部署**: GitHub Actions 使用 OIDC 进行身份验证，无需存储长期凭据
5. **隔离工作进程模型**: 使用 .NET 9 隔离工作进程以获得更好的性能和灵活性

## 先决条件

在开始之前，请确保已安装以下工具:

### 必需工具

| 工具 | 版本 | 安装命令 (macOS) |
|-----|------|-----------------|
| **.NET SDK** | 9.0.x | [下载](https://dotnet.microsoft.com/download/dotnet/9.0) |
| **Azure Functions Core Tools** | v4 | `brew tap azure/functions && brew install azure-functions-core-tools@4` |
| **Azure CLI** | 最新 | `brew install azure-cli` |
| **GitHub CLI** | 最新 | `brew install gh` |

### 验证安装

```bash
dotnet --version          # 应显示 9.x.x
func --version           # 应显示 4.x.x
az --version             # 应显示 Azure CLI 版本
gh --version             # 应显示 GitHub CLI 版本
```

### Azure 订阅要求

- 有效的 Azure 订阅
- 订阅中的贡献者权限或更高权限
- 能够创建 Azure AD 应用注册和服务主体
- 能够分配 RBAC 角色

## 完整部署步骤

以下是从零开始部署整个项目的详细步骤说明。

### 步骤 1: 安装必需工具

```bash
# 安装 Azure Functions Core Tools (需要先添加 tap)
brew tap azure/functions
brew install azure-functions-core-tools@4

# 安装 Azure CLI
brew install azure-cli

# 安装 GitHub CLI (用于配置仓库密钥)
brew install gh
```

### 步骤 2: Azure 登录

```bash
# 登录到 Azure
az login

# 验证当前订阅
az account show

# 如果需要切换订阅
az account set --subscription "您的订阅名称或ID"
```

### 步骤 3: 注册 Azure 资源提供程序

Azure 订阅需要注册相应的资源提供程序才能创建资源。运行以下命令:

```bash
# 注册 Key Vault 资源提供程序 (用于创建 Key Vault)
az provider register --namespace Microsoft.KeyVault

# 注册 Web 资源提供程序 (用于创建 Function App 和 App Service Plan)
az provider register --namespace Microsoft.Web

# 注册存储资源提供程序 (用于创建 Storage Account)
az provider register --namespace Microsoft.Storage

# 验证注册状态 (应显示 "Registered")
az provider show --namespace Microsoft.KeyVault --query "registrationState"
az provider show --namespace Microsoft.Web --query "registrationState"
az provider show --namespace Microsoft.Storage --query "registrationState"
```

注意: 资源提供程序注册可能需要几分钟时间。

### 步骤 4: 创建资源组

```bash
# 在加拿大中部区域创建资源组
# 注意: 我们选择 canadacentral 是因为 eastus/eastus2 的 VM 配额为 0
az group create \
  --name rg-kvfuncapp-cc \
  --location canadacentral
```

输出示例:
```json
{
  "id": "/subscriptions/.../resourceGroups/rg-kvfuncapp-cc",
  "location": "canadacentral",
  "name": "rg-kvfuncapp-cc",
  "properties": {
    "provisioningState": "Succeeded"
  }
}
```

### 步骤 5: 创建 Azure AD 应用注册和服务主体

为了让 GitHub Actions 能够部署到 Azure，我们需要创建一个应用注册和对应的服务主体。

```bash
# 创建应用注册
az ad app create --display-name github-deploy

# 获取应用 ID (Application/Client ID)
APP_ID=$(az ad app list --display-name github-deploy --query "[0].appId" -o tsv)
echo "Application (Client) ID: $APP_ID"

# 创建服务主体 (Service Principal)
az ad sp create --id $APP_ID

# 获取服务主体对象 ID
SP_OID=$(az ad sp show --id $APP_ID --query id -o tsv)
echo "Service Principal Object ID: $SP_OID"
```

**说明:**
- **APP_ID** (应用程序 ID): 用于 OIDC 认证，将作为 GitHub Secret `AZURE_CLIENT_ID`
- **SP_OID** (服务主体对象 ID): 用于授予 RBAC 角色权限

### 步骤 6: 授予服务主体必要的权限

服务主体需要两个关键角色才能完成部署:

```bash
# 获取订阅 ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# 1. 授予 Contributor 角色 (用于创建和管理资源)
az role assignment create \
  --assignee $SP_OID \
  --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc

# 2. 授予 "Role Based Access Control Administrator" 角色
#    (用于在 Bicep 中创建角色分配，例如授予 Function App 访问 Key Vault 的权限)
az role assignment create \
  --assignee $SP_OID \
  --role "Role Based Access Control Administrator" \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc
```

**为什么需要两个角色?**
- **Contributor**: 允许创建和管理 Azure 资源（Function App、Key Vault、Storage 等）
- **Role Based Access Control Administrator**: 允许在 Bicep 部署过程中创建角色分配（例如授予 Function App 的托管标识访问 Key Vault 的权限）

如果没有第二个角色，Bicep 部署会失败，错误信息为 "The client does not have authorization to perform action 'Microsoft.Authorization/roleAssignments/write'"。

### 步骤 7: 创建 OIDC 联合凭据

OIDC (OpenID Connect) 允许 GitHub Actions 使用短期令牌进行身份验证，无需存储长期密钥。

```bash
# 获取租户 ID
TENANT_ID=$(az account show --query tenantId -o tsv)

# 创建联合凭据 (针对 main 分支)
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:Gilbertbec/ClaudeTest:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

**subject 字段说明:**
- 格式: `repo:<OWNER>/<REPO>:ref:refs/heads/<BRANCH>`
- 示例: `repo:Gilbertbec/ClaudeTest:ref:refs/heads/main`
- 将 `<OWNER>` 和 `<REPO>` 替换为您的 GitHub 用户名和仓库名
- 此配置仅允许来自 `main` 分支的部署

### 步骤 8: 配置 GitHub 仓库密钥

使用 GitHub CLI 将必要的凭据存储为仓库密钥:

```bash
# 先登录 GitHub (如果尚未登录)
gh auth login

# 切换到您的仓库目录
cd /path/to/ClaudeTest

# 设置 Azure Client ID (应用程序 ID)
gh secret set AZURE_CLIENT_ID --body "$APP_ID"

# 设置 Azure Tenant ID
gh secret set AZURE_TENANT_ID --body "$TENANT_ID"

# 设置 Azure Subscription ID
gh secret set AZURE_SUBSCRIPTION_ID --body "$SUBSCRIPTION_ID"

# 验证密钥已设置
gh secret list
```

这三个密钥将被 GitHub Actions 工作流使用，以通过 OIDC 对 Azure 进行身份验证。

### 步骤 9: 部署基础设施

有两种方式部署基础设施:

#### 方式 A: 通过 GitHub Actions 自动部署 (推荐)

```bash
# 提交并推送代码到 main 分支
git add .
git commit -m "Initial deployment"
git push origin main
```

GitHub Actions 工作流 (`.github/workflows/deploy.yml`) 将自动:
1. 验证和部署 Bicep 基础设施
2. 构建 .NET 9 Function 应用
3. 将应用部署到 Azure Functions

#### 方式 B: 手动部署

```bash
# 使用 Azure CLI 手动部署
az deployment group create \
  --resource-group rg-kvfuncapp-cc \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

部署完成后，输出将包含:
- `functionAppName`: Function App 的名称
- `functionAppUrl`: Function App 的 URL
- `keyVaultName`: Key Vault 的名称
- `keyVaultUri`: Key Vault 的 URI

### 步骤 10: 授予您自己访问 Key Vault 的权限

默认情况下，即使是订阅所有者也无法直接访问启用了 RBAC 的 Key Vault。您需要为自己分配角色才能创建和管理机密。

```bash
# 获取您自己的用户对象 ID
MY_USER_OID=$(az ad signed-in-user show --query id -o tsv)

# 授予 "Key Vault Secrets Officer" 角色 (可以创建、读取、更新、删除机密)
az role assignment create \
  --assignee $MY_USER_OID \
  --role "Key Vault Secrets Officer" \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc/providers/Microsoft.KeyVault/vaults/kv-kvfuncapp
```

**角色说明:**
- **Key Vault Secrets Officer**: 对机密的完全管理权限（创建、读取、更新、删除）
- **Key Vault Secrets User**: 只读权限（Function App 使用此角色）

如果不执行此步骤，在尝试创建机密时会收到 "Forbidden" 错误。

### 步骤 11: 创建测试机密

现在您可以在 Key Vault 中创建测试机密:

```bash
# 创建一个名为 MySecret 的机密
az keyvault secret set \
  --vault-name kv-kvfuncapp \
  --name MySecret \
  --value "Hello from Key Vault"

# 验证机密已创建
az keyvault secret show \
  --vault-name kv-kvfuncapp \
  --name MySecret \
  --query "value" -o tsv
```

输出应显示: `Hello from Key Vault`

### 部署验证

验证所有资源已成功创建:

```bash
# 列出资源组中的所有资源
az resource list \
  --resource-group rg-kvfuncapp-cc \
  --output table

# 检查 Function App 的托管标识
az functionapp identity show \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc

# 检查 Key Vault 的角色分配
az role assignment list \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc/providers/Microsoft.KeyVault/vaults/kv-kvfuncapp \
  --output table
```

## 创建的所有 Azure 资源

以下是项目创建的所有 Azure 资源及其详细信息:

### 1. 资源组 (Resource Group)

- **名称**: `rg-kvfuncapp-cc`
- **位置**: Canada Central
- **用途**: 所有项目资源的逻辑容器

### 2. 存储账户 (Storage Account)

- **名称**: `stkvfuncapp[uniquestring]` (例如: `stkvfuncapp7x3k9m`)
- **SKU**: Standard_LRS (本地冗余存储)
- **种类**: StorageV2
- **用途**:
  - Azure Functions 运行时所需
  - 存储函数代码、主机配置
  - 存储执行日志和状态
- **安全**: 仅 HTTPS 流量，TLS 1.2 最低版本
- **命名逻辑**: 使用 `uniqueString(resourceGroup().id)` 确保全局唯一性

### 3. 应用服务计划 (App Service Plan)

- **名称**: `asp-kvfuncapp`
- **SKU**: Y1 (Consumption / Dynamic)
- **操作系统**: Windows
- **用途**: 托管 Azure Functions 的计算资源
- **计费模式**: 按执行次数和执行时间付费
- **特性**: 自动扩展，空闲时为零成本

### 4. 函数应用 (Function App)

- **名称**: `func-kvfuncapp`
- **运行时**: .NET 9 (isolated worker process)
- **函数版本**: ~4
- **身份**: 系统分配的托管标识
- **配置**:
  - `AzureWebJobsStorage`: 存储账户连接字符串
  - `FUNCTIONS_EXTENSION_VERSION`: ~4
  - `FUNCTIONS_WORKER_RUNTIME`: dotnet-isolated
  - `WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED`: 1 (优化冷启动)
  - `KEY_VAULT_URI`: Key Vault 的 URI
- **安全**: 仅 HTTPS
- **框架版本**: v9.0

### 5. Key Vault

- **名称**: `kv-kvfuncapp`
- **SKU**: Standard
- **授权模式**: Azure RBAC (非访问策略)
- **软删除**: 启用，保留期 7 天
- **用途**: 安全存储应用程序机密、密钥、证书
- **访问**: 通过 RBAC 角色分配控制

### 6. Key Vault 角色分配 (Role Assignment)

- **角色**: Key Vault Secrets User (`4633458b-17de-408a-b874-0445c86b69e6`)
- **被授权者**: Function App 的系统分配托管标识
- **范围**: Key Vault 资源
- **权限**: 允许读取机密值
- **命名**: 使用 `guid()` 函数根据资源 ID 生成确定性名称

### 7. Azure AD 应用注册 (App Registration)

- **名称**: `github-deploy`
- **用途**: GitHub Actions OIDC 身份验证
- **凭据类型**: 联合凭据 (Federated Credential)
- **联合配置**:
  - 颁发者: `https://token.actions.githubusercontent.com`
  - 主题: `repo:Gilbertbec/ClaudeTest:ref:refs/heads/main`
  - 受众: `api://AzureADTokenExchange`
- **角色分配**:
  - Contributor (资源组范围)
  - Role Based Access Control Administrator (资源组范围)

### 资源关系图

```
rg-kvfuncapp-cc (Resource Group)
├── stkvfuncapp[unique] (Storage Account)
│   └── 连接到 → func-kvfuncapp
├── asp-kvfuncapp (App Service Plan - Y1 Consumption)
│   └── 托管 → func-kvfuncapp
├── func-kvfuncapp (Function App)
│   ├── 托管标识 → 角色分配 → kv-kvfuncapp
│   └── 环境变量 KEY_VAULT_URI → kv-kvfuncapp
└── kv-kvfuncapp (Key Vault)
    ├── 启用 RBAC 授权
    └── 包含机密 (例如: MySecret)

github-deploy (App Registration)
└── OIDC 联合凭据 → GitHub Actions
    └── 部署到 → rg-kvfuncapp-cc
```

## GitHub Actions 工作流

项目包含两个 CI/CD 工作流，实现完全自动化的基础设施和应用程序部署。

### 工作流 1: deploy.yml - 主部署流水线

**文件路径**: `.github/workflows/deploy.yml`

**触发条件**:
- 推送到 `main` 分支
- 手动触发 (`workflow_dispatch`)

**权限**:
- `id-token: write` - 用于 OIDC 令牌交换
- `contents: read` - 读取仓库内容

**作业流程**:

1. **deploy-infra** (部署基础设施)
   - 使用 OIDC 登录 Azure
   - 部署 Bicep 模板和参数文件
   - 创建/更新所有 Azure 资源

2. **build-and-deploy** (构建和部署应用)
   - 依赖于 `deploy-infra` 作业完成
   - 设置 .NET 9 环境
   - 使用 `dotnet publish` 构建发布包
   - 登录 Azure
   - 使用 `Azure/functions-action@v1` 部署到 Function App

**环境变量**:
```yaml
AZURE_RESOURCE_GROUP: rg-kvfuncapp-cc
BICEP_FILE: infra/main.bicep
BICEP_PARAMS: infra/main.bicepparam
FUNCTION_APP_NAME: func-kvfuncapp
FUNCTION_PROJECT_PATH: src/KeyVaultFunction
DOTNET_VERSION: '9.0.x'
```

### 工作流 2: bicep-deploy.yml - 基础设施验证和部署

**文件路径**: `.github/workflows/bicep-deploy.yml`

**触发条件**:
- 推送到 `main` 分支且修改了 `infra/**` 路径下的文件
- 针对 `main` 分支的 PR 修改了 `infra/**` 路径下的文件
- 手动触发

**作业流程**:

1. **validate** (验证阶段)
   - 运行 `az bicep build` 进行 Bicep 语法检查
   - 确保资源组存在
   - 运行 `az deployment group validate` 验证模板
   - 运行 `az deployment group what-if` 预览变更

2. **deploy** (部署阶段)
   - 仅在 `main` 分支推送时执行（PR 时跳过）
   - 需要手动批准 (environment: production)
   - 确保资源组存在
   - 部署 Bicep 模板

**关键特性**:
- **路径过滤**: 仅在基础设施代码变更时触发
- **What-if 分析**: PR 时显示计划变更，无需实际部署
- **环境保护**: 生产部署需要手动批准
- **验证优先**: 在部署前验证模板语法和有效性

### OIDC 身份验证流程

```
GitHub Actions Workflow
    │
    ├─ Request OIDC token from GitHub
    │  (subject: repo:Gilbertbec/ClaudeTest:ref:refs/heads/main)
    │
    ├─ azure/login@v2 action
    │  ├─ AZURE_CLIENT_ID (from secret)
    │  ├─ AZURE_TENANT_ID (from secret)
    │  └─ AZURE_SUBSCRIPTION_ID (from secret)
    │
    └─> Azure AD verifies federated credential
        └─> Issues Azure access token
            └─> GitHub Actions uses token for az/ARM deployments
```

### 查看工作流状态

```bash
# 使用 GitHub CLI 查看最近的工作流运行
gh run list

# 查看特定运行的详细信息
gh run view <RUN_ID>

# 实时查看运行日志
gh run watch
```

## 本地开发

### 配置本地环境

1. **更新本地配置文件**

编辑 `src/KeyVaultFunction/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "KEY_VAULT_URI": "https://kv-kvfuncapp.vault.azure.net/"
  }
}
```

将 `KEY_VAULT_URI` 替换为您实际的 Key Vault URI。

2. **使用 Azure CLI 进行本地身份验证**

本地开发时，`DefaultAzureCredential` 会使用您的 Azure CLI 凭据:

```bash
# 登录 Azure
az login

# 验证您的身份
az account show
```

确保您的 Azure 账户已被授予 Key Vault 的 "Key Vault Secrets Officer" 或 "Key Vault Secrets User" 角色。

### 启动本地 Functions 主机

```bash
# 切换到函数项目目录
cd src/KeyVaultFunction

# 启动 Functions 主机
func start
```

输出示例:
```
Azure Functions Core Tools
Core Tools Version:       4.0.5907 Commit hash: N/A +87c8c0feab6314322f0aeec9a945709968ec4fef (64-bit)
Function Runtime Version: 4.834.3.22875

Functions:

        GetSecret: [GET] http://localhost:7071/api/GetSecret

For detailed output, run func with --verbose flag.
```

### 本地测试

```bash
# 测试成功场景 - 获取存在的机密
curl "http://localhost:7071/api/GetSecret?secretName=MySecret"
# 预期输出: Hello from Key Vault

# 测试验证 - 缺少参数
curl "http://localhost:7071/api/GetSecret"
# 预期输出: Please pass a 'secretName' query parameter.

# 测试错误处理 - 不存在的机密
curl "http://localhost:7071/api/GetSecret?secretName=DoesNotExist"
# 预期输出: 500 错误和堆栈跟踪
```

### 本地调试 (使用 Visual Studio Code)

1. 安装 Azure Functions 扩展
2. 在 VSCode 中打开项目
3. 设置断点
4. 按 F5 启动调试
5. 使用 curl 或浏览器发送请求

`.vscode/launch.json` 配置示例:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Attach to .NET Functions",
      "type": "coreclr",
      "request": "attach",
      "processId": "${command:azureFunctions.pickProcess}"
    }
  ]
}
```

## 测试

项目包含单元测试框架，使用 xUnit、Moq 和 FluentAssertions。

### 测试项目结构

- **项目**: `src/KeyVaultFunction.Tests/KeyVaultFunction.Tests.csproj`
- **框架**: .NET 9
- **测试库**:
  - xUnit 2.9.2 (测试框架)
  - Moq 4.20.72 (模拟对象)
  - FluentAssertions 6.12.2 (流畅断言)
  - coverlet.collector 6.0.2 (代码覆盖率)

### 运行单元测试

```bash
# 运行所有非集成测试
dotnet test --filter "Category!=Integration"

# 运行所有测试（包括集成测试）
dotnet test

# 运行仅集成测试
dotnet test --filter "Category=Integration"

# 运行测试并生成代码覆盖率报告
dotnet test /p:CollectCoverage=true /p:CoverageReporter=html
```

### 手动端到端测试

部署后，使用以下命令测试已部署的函数:

```bash
# 1. 获取函数的调用 URL
FUNC_URL=$(az functionapp function show \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc \
  --function-name GetSecret \
  --query invokeUrlTemplate -o tsv)

# 2. 获取函数访问密钥
FUNC_KEY=$(az functionapp function keys list \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc \
  --function-name GetSecret \
  --query default -o tsv)

# 3. 测试检索机密
curl "${FUNC_URL}?secretName=MySecret&code=${FUNC_KEY}"
# 预期输出: Hello from Key Vault

# 4. 测试缺少参数 (应返回 400)
curl "${FUNC_URL}?code=${FUNC_KEY}"
# 预期输出: Please pass a 'secretName' query parameter.

# 5. 测试不存在的机密 (应返回 500)
curl "${FUNC_URL}?secretName=DoesNotExist&code=${FUNC_KEY}"
# 预期输出: 500 错误和异常堆栈跟踪
```

### 测试场景

| 测试场景 | HTTP 方法 | 查询参数 | 预期响应 | 状态码 |
|---------|----------|---------|---------|--------|
| 成功检索机密 | GET | `secretName=MySecret` | `"Hello from Key Vault"` | 200 |
| 缺少 secretName | GET | (无) | `"Please pass a 'secretName' query parameter."` | 400 |
| 不存在的机密 | GET | `secretName=DoesNotExist` | 异常堆栈跟踪 | 500 |
| 权限不足 | GET | `secretName=MySecret` | Forbidden 错误 | 500 |

### 日志和监控

```bash
# 实时查看函数日志
az webapp log tail \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc

# 查看最近的日志
az webapp log show \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc
```

## 遇到的问题和解决方案

在项目开发和部署过程中，我们遇到了多个问题。以下是详细的问题描述和解决方案，供参考。

### 问题 1: .NET 版本不匹配 (8 vs 9)

**现象**:
- 项目最初针对 .NET 8，但本地安装了 .NET 9 SDK
- 运行时出现版本不兼容错误

**原因**:
- `.csproj` 文件中 `<TargetFramework>net8.0</TargetFramework>` 与本地 SDK 不匹配

**解决方案**:
```xml
<!-- 在 KeyVaultFunction.csproj 和 KeyVaultFunction.Tests.csproj 中修改 -->
<TargetFramework>net9.0</TargetFramework>
```

同时更新 Function App 的 Bicep 配置:
```bicep
siteConfig: {
  netFrameworkVersion: 'v9.0'
  // ...
}
```

**教训**: 保持本地开发环境与目标运行时版本一致。

---

### 问题 2: Azure Functions Worker SDK 包版本过旧

**现象**:
```
error NETSDK1083: The specified RuntimeIdentifier 'win-x64' is not recognized.
```

**原因**:
- 旧版本的 Worker SDK 包不支持 .NET 9
- NuGet 包版本不兼容

**解决方案**:
更新所有 Azure Functions Worker 相关包到最新版本:

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.51.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" Version="2.1.0" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.0.7" />
```

**验证命令**:
```bash
dotnet restore
dotnet build
```

---

### 问题 3: .sln 文件干扰 func start

**现象**:
- 运行 `func start` 时尝试构建整个解决方案
- 包括测试项目等不必要的项目
- 导致构建错误或性能问题

**原因**:
- 存在 `.sln` 文件时，Functions Core Tools 会尝试构建整个解决方案
- 不必要的依赖和构建复杂性

**解决方案**:
```bash
# 删除解决方案文件
rm KeyVaultFunction.sln

# 直接在项目目录中运行
cd src/KeyVaultFunction
func start
```

**替代方案**: 保留 `.sln` 文件但在 `func start` 时指定项目文件:
```bash
func start --csharp --project KeyVaultFunction.csproj
```

**教训**: 对于简单项目，避免使用解决方案文件；对于复杂项目，明确指定要构建的项目。

---

### 问题 4: Bicep 语法错误 - 分号 vs 逗号

**现象**:
```
Error BCP012: Expected the "}" character at this location.
```

**原因**:
- 在 Bicep 数组中错误使用分号 (`;`) 而不是逗号 (`,`)
- 混淆了 JSON 和 Bicep 的语法

**错误代码**:
```bicep
appSettings: [
  { name: 'AzureWebJobsStorage', value: storageAccountConnectionString };
  { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' };
]
```

**正确代码**:
```bicep
appSettings: [
  { name: 'AzureWebJobsStorage', value: storageAccountConnectionString },
  { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
]
```

**教训**:
- Bicep 数组元素使用逗号分隔（类似 JSON）
- 最后一个元素后不需要逗号

---

### 问题 5: Azure 资源提供程序未注册

**现象**:
```
Code: MissingSubscriptionRegistration
Message: The subscription is not registered to use namespace 'Microsoft.KeyVault'.
```

**原因**:
- 新订阅默认不注册所有资源提供程序
- 部署时尝试创建未注册命名空间的资源

**解决方案**:
```bash
# 注册所有需要的资源提供程序
az provider register --namespace Microsoft.KeyVault
az provider register --namespace Microsoft.Web
az provider register --namespace Microsoft.Storage

# 等待注册完成（可能需要几分钟）
az provider show --namespace Microsoft.KeyVault --query "registrationState"
```

**教训**: 在新订阅中部署前，先注册所有需要的资源提供程序。

---

### 问题 6: Storage Account 名称冲突

**现象**:
```
StorageAccountAlreadyExists: The storage account named 'stkvfuncapp' is already taken.
```

**原因**:
- Storage Account 名称必须全局唯一
- 多次部署或多个用户使用相同的 `baseName` 会导致冲突

**解决方案**:
在 `storageAccount.bicep` 中添加 `uniqueString()` 后缀:

```bicep
var uniqueSuffix = uniqueString(resourceGroup().id)
var rawName = replace(toLower('st${baseName}${uniqueSuffix}'), '-', '')
var storageAccountName = substring(rawName, 0, min(length(rawName), 24))

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  // ...
}
```

**说明**:
- `uniqueString(resourceGroup().id)` 基于资源组 ID 生成确定性的哈希
- 移除连字符确保符合存储账户命名规则
- 截断到 24 个字符符合最大长度限制

---

### 问题 7: VM 配额为 0 (eastus/eastus2)

**现象**:
```
Deployment failed. Correlation ID: xxx. {
  "error": {
    "code": "QuotaExceeded",
    "message": "Operation results in exceeding quota limits. Max allowed: 0."
  }
}
```

**原因**:
- 某些 Azure 区域的消费型计划有 VM 配额限制
- `eastus` 和 `eastus2` 对新订阅可能有配额限制

**解决方案**:
切换到配额充足的区域:

```bash
# 在 main.bicepparam 中修改
param location = 'canadacentral'

# 重新创建资源组
az group delete --name rg-kvfuncapp --yes
az group create --name rg-kvfuncapp-cc --location canadacentral

# 重新部署
az deployment group create \
  --resource-group rg-kvfuncapp-cc \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

**可选区域** (通常配额充足):
- `canadacentral`
- `westus2`
- `northeurope`
- `westeurope`

**检查配额**:
```bash
az vm list-usage --location eastus -o table
az vm list-usage --location canadacentral -o table
```

---

### 问题 8: 服务主体无法创建角色分配

**现象**:
```
Status Message: The client '<SP_OID>' with object id '<SP_OID>'
does not have authorization to perform action
'Microsoft.Authorization/roleAssignments/write' over scope '...'
```

**原因**:
- 服务主体只有 `Contributor` 角色
- `Contributor` 无法创建角色分配
- Bicep 模板中需要创建 Function App 到 Key Vault 的角色分配

**解决方案**:
为服务主体添加 "Role Based Access Control Administrator" 角色:

```bash
az role assignment create \
  --assignee $SP_OID \
  --role "Role Based Access Control Administrator" \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc
```

**角色对比**:
| 角色 | 权限 |
|-----|------|
| Contributor | 创建/管理资源，但不能授予访问权限 |
| Role Based Access Control Administrator | 可以创建/删除角色分配 |
| Owner | 完全访问权限（包括角色分配） |

**教训**: 对于需要在部署过程中创建角色分配的 IaC，服务主体需要 RBAC 管理权限。

---

### 问题 9: 用户无法向 Key Vault 写入机密

**现象**:
```
Code: Forbidden
Message: The user, group or application 'appid=...;oid=...'
does not have secrets set permission on key vault 'kv-kvfuncapp'.
```

**原因**:
- Key Vault 启用了 RBAC 授权
- 即使是订阅所有者也需要显式授予角色
- 默认没有对 Key Vault 的访问权限

**解决方案**:
为用户授予 "Key Vault Secrets Officer" 角色:

```bash
# 获取当前用户的对象 ID
MY_USER_OID=$(az ad signed-in-user show --query id -o tsv)

# 授予权限
az role assignment create \
  --assignee $MY_USER_OID \
  --role "Key Vault Secrets Officer" \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc/providers/Microsoft.KeyVault/vaults/kv-kvfuncapp
```

**Key Vault RBAC 角色**:
| 角色 | 机密权限 |
|-----|---------|
| Key Vault Secrets Officer | 完全管理（创建、读取、更新、删除） |
| Key Vault Secrets User | 只读 |
| Key Vault Administrator | 所有对象的完全管理 |

**验证**:
```bash
# 角色分配生效后创建机密
az keyvault secret set \
  --vault-name kv-kvfuncapp \
  --name MySecret \
  --value "Hello from Key Vault"
```

---

### 最佳实践总结

基于遇到的问题，以下是关键最佳实践:

1. **版本一致性**: 保持本地 SDK、项目目标框架、Azure 运行时版本一致
2. **资源命名**: 对全局唯一资源使用 `uniqueString()` 函数
3. **权限规划**: 提前规划 RBAC 权限，包括部署服务主体和最终用户
4. **区域选择**: 选择配额充足的区域，避免临时性限制
5. **资源提供程序**: 在新订阅中提前注册所有需要的命名空间
6. **语法验证**: 使用 `az bicep build` 在本地验证 Bicep 模板
7. **逐步验证**: 分阶段部署和测试，避免一次性部署复杂配置

## 清理和销毁资源

当您不再需要这些资源时，可以完全清理以避免产生费用。

### 删除 Azure 资源组

这将删除资源组及其包含的所有资源（Function App、Key Vault、Storage Account 等）:

```bash
# 删除资源组（异步，立即返回）
az group delete \
  --name rg-kvfuncapp-cc \
  --yes \
  --no-wait

# 检查删除状态
az group exists --name rg-kvfuncapp-cc
# 输出: false (删除完成)

# 或监控删除进度
az group wait \
  --name rg-kvfuncapp-cc \
  --deleted
```

**注意**:
- Key Vault 有软删除保护，删除后会保留 7 天
- 在保留期内，Key Vault 名称仍被占用，无法创建同名 Key Vault
- 如需立即重用名称，必须清除软删除的 Key Vault（见下文）

### 清除软删除的 Key Vault (可选)

如果需要立即重用 Key Vault 名称:

```bash
# 列出软删除的 Key Vault
az keyvault list-deleted --resource-type vault -o table

# 永久清除（不可恢复）
az keyvault purge \
  --name kv-kvfuncapp \
  --location canadacentral
```

**警告**: 清除操作不可逆，将永久删除所有机密。

### 删除 Azure AD 应用注册

删除用于 GitHub Actions OIDC 的应用注册和服务主体:

```bash
# 获取应用 ID (如果已丢失)
APP_ID=$(az ad app list --display-name github-deploy --query "[0].appId" -o tsv)

# 删除应用注册（会自动删除关联的服务主体）
az ad app delete --id $APP_ID

# 验证删除
az ad app list --display-name github-deploy
# 应返回空列表
```

### 清理 GitHub 仓库配置

```bash
# 删除 GitHub Secrets
gh secret remove AZURE_CLIENT_ID
gh secret remove AZURE_TENANT_ID
gh secret remove AZURE_SUBSCRIPTION_ID

# 验证
gh secret list
# 应不再显示 Azure 相关密钥
```

### 删除 GitHub 仓库 (可选)

如果整个项目仓库不再需要:

```bash
# 删除 GitHub 仓库（需要确认）
gh repo delete Gilbertbec/ClaudeTest --yes

# 或通过 GitHub 网页界面删除:
# 1. 访问 https://github.com/Gilbertbec/ClaudeTest/settings
# 2. 滚动到 "Danger Zone"
# 3. 点击 "Delete this repository"
# 4. 输入仓库名称确认
```

### 完整清理脚本

以下脚本执行完整清理:

```bash
#!/bin/bash
set -e

echo "开始清理 Azure 资源..."

# 获取必要的 ID
APP_ID=$(az ad app list --display-name github-deploy --query "[0].appId" -o tsv)

# 删除资源组
echo "删除资源组..."
az group delete --name rg-kvfuncapp-cc --yes --no-wait

# 等待资源组删除完成
echo "等待资源组删除完成..."
az group wait --name rg-kvfuncapp-cc --deleted || true

# 清除软删除的 Key Vault
echo "清除软删除的 Key Vault..."
az keyvault purge --name kv-kvfuncapp --location canadacentral 2>/dev/null || true

# 删除应用注册
echo "删除应用注册..."
az ad app delete --id $APP_ID 2>/dev/null || true

# 清理 GitHub Secrets
echo "清理 GitHub Secrets..."
gh secret remove AZURE_CLIENT_ID 2>/dev/null || true
gh secret remove AZURE_TENANT_ID 2>/dev/null || true
gh secret remove AZURE_SUBSCRIPTION_ID 2>/dev/null || true

echo "清理完成！"
```

保存为 `cleanup.sh`，添加执行权限并运行:

```bash
chmod +x cleanup.sh
./cleanup.sh
```

### 验证清理结果

```bash
# 验证资源组已删除
az group exists --name rg-kvfuncapp-cc
# 输出: false

# 验证应用注册已删除
az ad app list --display-name github-deploy
# 输出: []

# 验证 GitHub Secrets 已删除
gh secret list
# 不应显示 AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID
```

### 成本说明

清理前的预估成本（每月）:
- **Function App** (消费型计划): $0 (空闲) ~ $0.20/百万次执行
- **Storage Account** (Standard_LRS): ~$0.05/GB
- **Key Vault**: $0.03/10,000 次操作
- **总计**: 低流量下 < $1/月

**重要**: 即使不使用，存储账户和 Key Vault 也会产生少量基础费用。建议测试完成后立即清理。

## 项目结构

```
ClaudeTest/
├── .github/
│   └── workflows/
│       ├── deploy.yml              # 主 CI/CD 流水线 (基础设施 + 应用)
│       └── bicep-deploy.yml        # 基础设施验证和部署流水线
├── infra/                          # 基础设施即代码 (Bicep)
│   ├── main.bicep                  # 主模板 (编排所有模块)
│   ├── main.bicepparam             # 参数文件 (baseName, location)
│   └── modules/
│       ├── storageAccount.bicep    # 存储账户模块
│       ├── appServicePlan.bicep    # 应用服务计划模块
│       ├── functionApp.bicep       # Function App 模块
│       ├── keyVault.bicep          # Key Vault 模块
│       └── keyVaultRoleAssignment.bicep  # RBAC 角色分配模块
├── src/
│   ├── KeyVaultFunction/           # 主应用项目
│   │   ├── GetSecret.cs            # HTTP 触发函数实现
│   │   ├── Program.cs              # 应用入口点 (配置 DI, SecretClient)
│   │   ├── KeyVaultFunction.csproj # 项目文件 (.NET 9, Azure Functions v4)
│   │   ├── host.json               # Functions 主机配置
│   │   └── local.settings.json     # 本地开发配置 (不提交)
│   └── KeyVaultFunction.Tests/     # 单元测试项目
│       └── KeyVaultFunction.Tests.csproj  # 测试项目文件 (xUnit, Moq, FluentAssertions)
├── CLAUDE.md                       # Claude Code 项目指南
└── README.md                       # 本文件 (项目完整文档)
```

## 技术栈

### 后端运行时

- **.NET 9** - 最新的 .NET 运行时
- **Azure Functions v4** - 无服务器计算平台
- **隔离工作进程模型** - 与主机进程隔离，提供更好的性能和灵活性

### Azure 服务

- **Azure Functions** - 无服务器计算
- **Azure Key Vault** - 机密管理
- **Azure Storage** - 持久化存储
- **Azure Managed Identity** - 无密钥身份验证
- **Azure RBAC** - 基于角色的访问控制

### NuGet 包

| 包名 | 版本 | 用途 |
|------|------|------|
| Microsoft.Azure.Functions.Worker | 2.51.0 | Functions Worker 核心 |
| Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | 2.1.0 | HTTP 触发器支持 |
| Microsoft.Azure.Functions.Worker.Sdk | 2.0.7 | Functions Worker SDK |
| Azure.Identity | 1.13.2 | Azure 身份验证（包括 DefaultAzureCredential） |
| Azure.Security.KeyVault.Secrets | 4.7.0 | Key Vault Secrets 客户端 |

### 测试框架

| 包名 | 版本 | 用途 |
|------|------|------|
| xunit | 2.9.2 | 测试框架 |
| Moq | 4.20.72 | 模拟对象 |
| FluentAssertions | 6.12.2 | 流畅断言 |
| coverlet.collector | 6.0.2 | 代码覆盖率 |

### 基础设施即代码

- **Bicep** - Azure ARM 模板的简化语言
- **Azure CLI** - 命令行工具
- **GitHub Actions** - CI/CD 自动化
- **OIDC** - 无密钥身份验证

## 安全性

项目实现了多层安全防护:

### 身份和访问管理

1. **托管标识**
   - 系统分配的托管标识自动管理
   - 无需存储或轮换凭据
   - 与 Azure AD 深度集成

2. **RBAC 最小权限原则**
   - Function App: 仅 "Key Vault Secrets User" (只读机密)
   - 部署服务主体: 仅资源组范围的权限
   - 用户: 按需授予最小必要权限

3. **OIDC 无密钥部署**
   - GitHub Actions 使用短期 OIDC 令牌
   - 无需在 GitHub Secrets 中存储长期凭据
   - 联合凭据限制为特定仓库和分支

### 数据保护

1. **传输加密**
   - 所有服务强制 HTTPS (`httpsOnly: true`)
   - TLS 1.2 最低版本
   - 存储账户仅允许 HTTPS 流量

2. **静态加密**
   - Key Vault 中的机密自动加密
   - 存储账户数据使用 Azure Storage Service Encryption

3. **机密管理**
   - 所有机密存储在 Key Vault
   - 连接字符串通过环境变量注入
   - `local.settings.json` 排除在版本控制外

### 网络安全

1. **出站安全**
   - Function App 仅通过 HTTPS 访问 Key Vault
   - 使用 Azure 内部网络（虚拟网络集成可选）

2. **入站安全**
   - Function 级别授权 (Function Key 或 Azure AD)
   - 可配置允许的 IP 范围（未在本项目中配置）

### Key Vault 安全

1. **软删除保护**
   - 保留期 7 天，防止意外删除
   - 清除前可恢复

2. **审计日志**
   - 所有访问操作记录到 Azure Monitor
   - 可配置诊断设置进行长期保留

### 最佳实践建议

1. **启用 Application Insights** 进行监控和告警
2. **配置虚拟网络集成** 隔离网络流量
3. **启用 Key Vault 防火墙** 限制访问来源
4. **使用 Azure AD 身份验证** 代替 Function Keys
5. **配置 Azure Policy** 强制执行安全标准
6. **定期审查角色分配** 确保最小权限

## 扩展和自定义

### 添加更多机密操作

扩展 Function App 支持创建、更新、删除机密:

```csharp
// 在 GetSecret.cs 中添加新方法
[Function("SetSecret")]
public async Task<IActionResult> SetSecret(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    string secretName = req.Query["secretName"];
    string secretValue = req.Query["secretValue"];

    await _secretClient.SetSecretAsync(secretName, secretValue);
    return new OkObjectResult($"Secret '{secretName}' set successfully.");
}
```

更新 Function App 的角色分配为 "Key Vault Secrets Officer":

```bicep
// 在 keyVaultRoleAssignment.bicep 中修改
var keyVaultSecretsOfficerRoleId = '......'  // Secrets Officer GUID
```

### 添加多个 Key Vault

修改 `main.bicep` 支持多个 Key Vault:

```bicep
module kv2 'modules/keyVault.bicep' = {
  name: 'keyVault2'
  params: {
    baseName: '${baseName}-secondary'
    location: location
  }
}
```

### 集成 Application Insights

在 `functionApp.bicep` 中添加:

```bicep
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${baseName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// 在 Function App 的 appSettings 中添加
{
  name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
  value: appInsights.properties.ConnectionString
}
```

### 添加自定义域和 SSL

```bicep
// 在 Function App 中配置自定义域
resource customDomain 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = {
  parent: functionApp
  name: 'api.yourdomain.com'
  properties: {
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: certificateThumbprint
  }
}
```

### 配置虚拟网络集成

```bicep
// 创建虚拟网络
resource vnet 'Microsoft.Network/virtualNetworks@2023-05-01' = {
  name: 'vnet-${baseName}'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: ['10.0.0.0/16']
    }
    subnets: [
      {
        name: 'function-subnet'
        properties: {
          addressPrefix: '10.0.1.0/24'
          delegations: [
            {
              name: 'Microsoft.Web.serverFarms'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

// 在 Function App 中启用 VNet 集成
virtualNetworkSubnetId: vnet.properties.subnets[0].id
```

## 故障排查

### 常见问题

#### 1. Function App 无法访问 Key Vault

**症状**: 500 错误，日志显示 "Forbidden" 或 "Unauthorized"

**检查清单**:
```bash
# 1. 验证托管标识已启用
az functionapp identity show \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc

# 2. 验证角色分配
az role assignment list \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc/providers/Microsoft.KeyVault/vaults/kv-kvfuncapp \
  --output table

# 3. 验证 Key Vault RBAC 已启用
az keyvault show \
  --name kv-kvfuncapp \
  --query "properties.enableRbacAuthorization"
# 应输出: true
```

#### 2. 本地开发时无法访问 Key Vault

**症状**: `DefaultAzureCredential` 认证失败

**解决方案**:
```bash
# 1. 确保已登录 Azure CLI
az login
az account show

# 2. 确保您有 Key Vault 权限
az role assignment create \
  --assignee $(az ad signed-in-user show --query id -o tsv) \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc/providers/Microsoft.KeyVault/vaults/kv-kvfuncapp

# 3. 检查 local.settings.json 中的 KEY_VAULT_URI
cat src/KeyVaultFunction/local.settings.json
```

#### 3. GitHub Actions 部署失败

**症状**: "The client does not have authorization..."

**检查 OIDC 配置**:
```bash
# 1. 验证 GitHub Secrets
gh secret list

# 2. 验证联合凭据
az ad app federated-credential list --id $APP_ID -o table

# 3. 验证服务主体角色
az role assignment list --assignee $SP_OID --output table
```

#### 4. 存储账户访问被拒绝

**症状**: Function App 无法启动，日志显示存储连接错误

**解决方案**:
```bash
# 验证连接字符串配置
az functionapp config appsettings list \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc \
  --query "[?name=='AzureWebJobsStorage'].value" -o tsv

# 重新部署 Bicep 修复连接字符串
az deployment group create \
  --resource-group rg-kvfuncapp-cc \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

### 日志和诊断

```bash
# 实时日志流
az webapp log tail \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc

# 下载日志文件
az webapp log download \
  --name func-kvfuncapp \
  --resource-group rg-kvfuncapp-cc \
  --log-file logs.zip

# 查看 Key Vault 审计日志 (需要先配置诊断设置)
az monitor activity-log list \
  --resource-id /subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-kvfuncapp-cc/providers/Microsoft.KeyVault/vaults/kv-kvfuncapp
```

## 参考资源

### Microsoft 官方文档

- [Azure Functions 文档](https://learn.microsoft.com/azure/azure-functions/)
- [Azure Key Vault 文档](https://learn.microsoft.com/azure/key-vault/)
- [托管标识文档](https://learn.microsoft.com/azure/active-directory/managed-identities-azure-resources/)
- [Bicep 文档](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [GitHub Actions for Azure](https://learn.microsoft.com/azure/developer/github/github-actions)

### 相关示例

- [Azure Functions .NET 隔离工作进程示例](https://github.com/Azure/azure-functions-dotnet-worker)
- [Key Vault RBAC 最佳实践](https://learn.microsoft.com/azure/key-vault/general/rbac-guide)
- [Bicep 模块示例](https://github.com/Azure/bicep/tree/main/docs/examples)

### 工具和 SDK

- [Azure Functions Core Tools](https://github.com/Azure/azure-functions-core-tools)
- [Azure CLI](https://learn.microsoft.com/cli/azure/)
- [GitHub CLI](https://cli.github.com/)
- [Azure SDK for .NET](https://github.com/Azure/azure-sdk-for-net)

## 许可证

本项目仅用于学习和演示目的。

## 作者

Claude Code 辅助创建

## 版本历史

- **v1.0.0** (2025-01-XX) - 初始版本
  - .NET 9 Azure Function
  - Key Vault 集成
  - 托管标识身份验证
  - RBAC 授权
  - Bicep IaC
  - GitHub Actions CI/CD
  - OIDC 无密钥部署

---

如有问题或需要帮助，请参考上述故障排查部分或查阅 Microsoft 官方文档。
