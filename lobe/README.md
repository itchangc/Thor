# Thor AI 前端项目 - Lobe Theme

一个基于 React + Vite + TypeScript + Ant Design 构建的现代化 AI 界面项目。

## 🚀 快速开始

### 环境要求

- Node.js 18+ 
- npm 或 yarn 包管理器
- 现代浏览器（支持 ES6+）

### 安装和运行

#### 1. 克隆项目
```bash
git clone <repository-url>
cd Thor/lobe
```

#### 2. 安装依赖
由于项目中存在React版本冲突问题，需要使用 `--legacy-peer-deps` 参数：

```bash
npm install --legacy-peer-deps
```

> **注意**: 如果遇到 ERESOLVE 错误，这是由于 `@lobehub/charts@2.0.0` 要求 React 19，而项目使用 React 18 导致的版本冲突。使用 `--legacy-peer-deps` 参数可以解决此问题。

#### 3. 环境变量配置
项目已包含 `.env` 文件，如需自定义配置，可以参考 `.env.example`：

```bash
# 如果需要重新配置环境变量
cp .env.example .env
```

#### 4. 启动开发服务器
```bash
npm run dev
```

服务器启动后，访问: http://localhost:5173

### 📦 可用脚本

```bash
# 开发模式启动
npm run dev

# 构建生产版本
npm run build

# 类型检查后构建
npm run build:check

# 代码检查
npm run lint

# 预览构建结果
npm run preview
```

## 🛠️ 技术栈

- **前端框架**: React 18
- **构建工具**: Vite 5
- **语言**: TypeScript
- **UI组件库**: Ant Design 5
- **样式方案**: Ant Design + Tailwind CSS + Styled Components
- **图标库**: @lobehub/icons, @ant-design/icons, Lucide React
- **图表库**: @lobehub/charts, @ant-design/plots, ECharts
- **路由**: React Router 6
- **国际化**: i18next + react-i18next
- **动画**: Framer Motion

## 🔧 常见问题

### 1. 依赖安装失败
**问题**: `npm install` 时出现 ERESOLVE 错误
```
npm ERR! ERESOLVE unable to resolve dependency tree
npm ERR! Could not resolve dependency:
npm ERR! peer react@"^19.0.0" from @lobehub/charts@2.0.0
```

**解决方案**: 使用 legacy peer deps 模式安装
```bash
npm install --legacy-peer-deps
```

### 2. 图标导入错误
**问题**: 控制台显示图标模块导入失败
```
The requested module does not provide an export named 'AssemblyAI'
```

**解决方案**: 已修复不存在的图标导入，确保只导入 `@lobehub/icons` 中实际存在的图标组件。

### 3. 清理重装
如果遇到其他依赖问题，可以尝试清理重装：
```bash
# 删除 node_modules 和 package-lock.json
rm -rf node_modules package-lock.json
# 或 Windows PowerShell 中使用
Remove-Item -Recurse -Force node_modules, package-lock.json

# 重新安装
npm install --legacy-peer-deps
```

### 4. 端口占用
如果 5173 端口被占用，Vite 会自动使用下一个可用端口，或者可以指定端口：
```bash
npm run dev -- --port 3000
```

## 📝 开发说明

### 项目结构
```
lobe/
├── src/
│   ├── components/     # 可复用组件
│   ├── pages/         # 页面组件
│   ├── utils/         # 工具函数
│   ├── styles/        # 样式文件
│   └── types/         # TypeScript 类型定义
├── public/            # 静态资源
├── index.html         # HTML 模板
├── package.json       # 依赖和脚本
├── vite.config.ts     # Vite 配置
└── tsconfig.json      # TypeScript 配置
```

### 代码规范
- 使用 ESLint 进行代码检查
- 使用 TypeScript 进行类型约束
- 遵循 React + Ant Design 最佳实践
- 使用 Ant Design 的主题系统进行样式定制

## 🤝 贡献指南

1. Fork 项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📄 许可证

本项目采用 Apache-2.0 许可证。详情请参阅 [LICENSE](../LICENSE) 文件。
