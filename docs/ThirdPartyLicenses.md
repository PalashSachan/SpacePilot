# SpacePilot — Third-Party Licenses & Attributions

SpacePilot utilizes open-source components under commercially friendly, permissive licenses (MIT and Apache 2.0). No components subject to viral copyleft licenses (e.g. GPL, AGPL) are included in this software.

---

## 1. Library Dependencies & Licensing Summary

| Package Name | Version | License | Project URL |
| :--- | :--- | :--- | :--- |
| **CommunityToolkit.Mvvm** | `8.4.2` | MIT | [GitHub](https://github.com/CommunityToolkit/dotnet) |
| **WPF-UI** | `4.3.0` | MIT | [GitHub](https://github.com/lepoco/wpfui) |
| **AWSSDK.S3** | `3.7.415.11` | Apache 2.0 | [GitHub](https://github.com/aws/aws-sdk-net) |
| **AWSSDK.Core** | `3.7.402.10` | Apache 2.0 | [GitHub](https://github.com/aws/aws-sdk-net) |
| **Microsoft.Data.Sqlite** | `10.0.3` | MIT | [GitHub](https://github.com/dotnet/efcore) |
| **Magick.NET-Q8-AnyCPU** | `14.5.0` | Apache 2.0 | [GitHub](https://github.com/dlemstra/Magick.NET) |
| **PdfSharpCore** | `1.3.67` | MIT | [GitHub](https://github.com/ststeiger/PdfSharpCore) |
| **Serilog** | `4.2.0` | Apache 2.0 | [GitHub](https://github.com/serilog/serilog) |
| **Serilog.Extensions.Logging** | `9.0.0` | Apache 2.0 | [GitHub](https://github.com/serilog/serilog-extensions-logging) |
| **Serilog.Sinks.File** | `6.0.0` | Apache 2.0 | [GitHub](https://github.com/serilog/serilog-sinks-file) |
| **Microsoft.Extensions.DependencyInjection** | `10.0.13` | MIT | [GitHub](https://github.com/dotnet/runtime) |
| **Microsoft.Extensions.Hosting** | `10.0.13` | MIT | [GitHub](https://github.com/dotnet/runtime) |
| **Microsoft.Extensions.Logging** | `10.0.13` | MIT | [GitHub](https://github.com/dotnet/runtime) |
| **xUnit** | `2.9.3` | Apache 2.0 | [GitHub](https://github.com/xunit/xunit) |
| **Moq** | `4.20.72` | BSD-3-Clause | [GitHub](https://github.com/moq/moq4) |
| **Obfuscar** | `2.2.38` | MIT | [GitHub](https://github.com/obfuscar/obfuscar) |

---

## 2. PDF Engine Licensing Evaluation Notice

During engineering evaluation, `PDFsharp 6.x` was found to enforce AGPL-3.0 copyleft obligations (`PdfSharpLicenseException`), which would mandate source-code disclosure for proprietary and enterprise installations.

To satisfy the application specification's strict non-restrictive licensing mandate, **PdfSharpCore** (version `1.3.67`) was selected. `PdfSharpCore` is licensed under the permissive **MIT License**, permitting unrestricted commercial use, embedding, and binary redistribution without copyleft encumbrances.

---

## 3. Full License Texts

### 3.1 The MIT License (MIT)
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

### 3.2 Apache License Version 2.0
Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at:

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
