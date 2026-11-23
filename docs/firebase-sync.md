# Firebase 同步

Mizu 可以把待办、单词本和常用显示偏好同步到你自己的 Firebase 项目。功能按需启用，使用「设置 → 同步 → 立即同步」触发，未登录时可以继续离线使用。

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

## 在两台电脑同步

1. 在第一台电脑打开「设置 → 同步」，导入配置，输入在该项目创建的邮箱和密码，点击「登录 Firebase」，然后点击「立即同步」。
2. 在另一台电脑用同一项目配置和同一 Firebase Auth 账号登录，点击「立即同步」。首次连接时，两边有不同数据会提示选择。要沿用第一台电脑的内容，选择云端版本。
3. 此后，只有一边有修改会直接同步。两边都有修改时，可以保留本机、保留云端或取消。下载会替换这几个同步项目，并留下上一份本机数据，可从同步页恢复。

同步范围包括待办与完成状态、单词与释义、模块顺序与常用显示开关、主题、玻璃浓度、组件和字体大小、单词本显示、待办显示偏好。PushPlus 凭据、Firebase 登录会话、窗口坐标、开机自启、天气位置、网络探测目标、硬件记录和缓存留在本机。

登录密码只在登录请求中使用。刷新令牌及连接配置用当前 Windows 用户的 DPAPI 加密保存。云端内容存于此账号对应的 Firestore 文档，项目管理员仍可在控制台管理数据。退出登录会移除本机连接，已上传内容保留在 Firestore。
