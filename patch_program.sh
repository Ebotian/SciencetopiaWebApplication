#!/bin/bash
# 找到数据库配置部分并添加条件判断
sed -i '/builder\.Services\.AddDbContext<ApplicationDbContext>/,/\);/ c\
// 根据环境选择数据库提供程序\
if (builder.Configuration.GetValue<bool>("UseInMemoryDatabase"))\
{\
    builder.Services.AddDbContext<ApplicationDbContext>(options =>\
        options.UseInMemoryDatabase("SciencetopiaDb"));\
    Console.WriteLine("使用内存数据库进行开发");\
}\
else\
{\
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");\
    builder.Services.AddDbContext<ApplicationDbContext>(options =>\
        options.UseSqlServer(connectionString, sqlServerOptionsAction: sqlOptions => \
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 5)));\
}' Program.cs
