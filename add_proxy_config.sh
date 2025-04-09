#!/bin/bash

# 找到HTTP客户端注册的位置
LINE_NUM=$(grep -n "services.AddHttpClient" Program.cs | cut -d':' -f1)

if [ -n "$LINE_NUM" ]; then
  # 添加代理配置代码
  sed -i "${LINE_NUM}i\\
// 添加HTTP客户端代理配置\\
if (builder.Configuration[\"HttpClientSettings:UseProxy\"] == \"true\")\\
{\\
    var proxyUrl = builder.Configuration[\"HttpClientSettings:ProxyAddress\"];\\
    if (!string.IsNullOrEmpty(proxyUrl))\\
    {\\
        builder.Services.AddHttpClient(\"ProxiedClient\").ConfigurePrimaryHttpMessageHandler(() =>\\
        {\\
            return new HttpClientHandler\\
            {\\
                Proxy = new WebProxy(proxyUrl),\\
                UseProxy = true\\
            };\\
        });\\
        Console.WriteLine(\$\"已配置HTTP代理: {proxyUrl}\");\\
    }\\
}\\
" Program.cs
  echo "已添加代理配置代码"
else
  echo "未找到HTTP客户端注册位置，请手动添加代理配置"
fi
