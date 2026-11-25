# Firebase 同步

Mizu 可以把待办、单词本和常用显示偏好备份到你自己的 Firebase 项目。使用「设置 → 同步」中的上传和恢复按钮，未登录时可以继续离线使用。

## 在 Firebase 控制台准备项目

1. 在自己的项目中添加一个网页应用。从项目设置复制它的 Firebase 配置。Mizu 支持粘贴 JSON、带 `firebaseConfig` 的 JavaScript 片段，或直接选择相应的 TXT、JSON、JS 文件。
2. 打开 Authentication，在登录方式中启用「电子邮件／密码」。在用户列表中添加一个自己的用户。这个用户就是 Mizu 同步时登录的账号。
3. 创建默认 Cloud Firestore 数据库。Mizu 使用原生模式的默认数据库和 Firebase Auth ID 令牌。
4. 在 Firestore 的「规则」中发布下方规则。已有其他应用使用这个数据库时，只将 `match /mizuSync/{uid}` 块加入现有 `match /databases/{database}/documents`，并检查现有通配规则没有给这条路径开放其他访问权限。规则用法见 [Firebase 安全规则文档](https://firebase.google.com/docs/firestore/security/rules-conditions)。

```text
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    match /mizuSync/{uid} {
      function owner() {
        return request.auth != null && request.auth.uid == uid;
      }
      function validSnapshot() {
        return request.resource.data.keys().hasAll(['schemaVersion', 'payload'])
          && request.resource.data.keys().hasOnly(['schemaVersion', 'payload'])
          && request.resource.data.schemaVersion == 1
          && request.resource.data.payload is bytes
          && request.resource.data.payload.size() <= 1000000;
      }
      allow get: if owner();
      allow create, update: if owner() && validSnapshot();
    }
  }
}
```

网页客户端配置使用网页 API Key。数据访问权限由 Firebase Auth 和规则共同控制。Mizu 通过 Google 官方的 Auth 和 Firestore HTTPS 接口连接。若项目启用了仅允许指定来源的 API Key 限制，请为桌面客户端准备可用于 Firebase Auth 的网页 API Key。仅支持邮箱和密码登录。相关机制见 [Firebase API Key 文档](https://firebase.google.com/docs/projects/api-keys)和 [Firestore REST 认证文档](https://firebase.google.com/docs/firestore/use-rest-api)。

## 上传和恢复

在「设置 → 同步」点击「导出网页配置」，可以把当前 Firebase 项目的连接配置保存为 JSON。网页版的「导入配置」可以直接读取这个文件，之后用同一 Firebase 邮箱和密码登录。文件只包含项目 ID 和网页 API Key。

1. 在「设置 → 同步」导入配置，用该项目的邮箱和密码登录。点击「上传备份至云端」，将当前本机内容存入 Firestore。云端保存最新一份备份。遇到其他设备更新的版本，手动上传需要先确认。
2. 使用同一项目和账号，在任意一台电脑点击「恢复云端备份」，按提示确认。云端备份会替换本机同步范围内的数据，原本机数据另存一份，可用「恢复下载前的本机数据」撤回。
3. 开启「自动备份至云端」后，可设 1–720 分钟的间隔。Mizu 运行时检查，本机内容和上次确认版本一样时跳过。遇到不同的云端版本只提示，恢复操作由你手动发起。

## 清理和测试恢复

先上传一份备份，确认成功后，展开「清空本地缓存或数据」。天气、诗词、外设读数缓存，以及单词本、待办或显示设置可以分别勾选。清理个人数据时，Mizu 会关闭自动备份，并单独保留清理前的本机副本。然后点击「恢复云端备份」，检查这些项目是否恢复。想取回本机清理前的副本时，使用「恢复清理前的本机数据」。

显示设置选项重置云端可恢复的常用外观与模块布局。天气城市、设备专用设置和 Firebase 连接等保留在本机。常规刷新会重新生成天气、诗词与外设读数缓存。

同步范围包括待办与完成状态、单词与释义、模块顺序与常用显示开关、主题、玻璃浓度、组件和字体大小、单词本显示、待办显示偏好。PushPlus 凭据、Firebase 登录会话、窗口坐标、开机自启、天气位置、网络探测目标、硬件记录和缓存留在本机。

登录密码只在登录请求中使用。刷新令牌及连接配置用当前 Windows 用户的 DPAPI 加密保存。云端内容存于此账号对应的 Firestore 文档，项目管理员仍可在控制台管理数据。退出登录会移除本机连接，已上传内容保留在 Firestore。
