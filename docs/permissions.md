# Permissions

Commands declare a permission string and require the invoking server principal to carry that permission or `*`. Principals are server-owned. The local console principal has `*`; ordinary principals have no permissions unless configured by trusted server code. Client data never defines principals.
