## 各分支说明：

### main: 

核心内容与E大“GameFramework”工程master分支保持一致：https://github.com/EllanJiang/GameFramework

但稍微修改了文件层架结构，日常使用更为方便

同时由于该项目主要是在Unity中使用，因此为了在替换“GameFramework.dll”为源码文件后依然可以正常使用，

这里增加“GameFramework.asmdef” —— 程序集定义文件，并勾选“Allow unsafe code”选项(在Unity中设置)

### develop：

日常开发的基础分支

### feature/detailComments:

主要用于代码查阅和注释，所有详细注释都在该分支
