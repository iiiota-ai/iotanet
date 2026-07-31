## dotnet常用命令


```sh
# 创建console项目
dotnet new console -n Rudp.Demo
```

```sh
# 运行项目
dotnet run
dotnet run -- receiver
dotnet run -- sender
#不重新编译运行项目
dotnet run --no-build -- receiver
dotnet run --no-build -- sender
```

```sh
# 构建项目
dotnet build
```

```sh
# 清理构建产物
dotnet clean
```
