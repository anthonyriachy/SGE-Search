<%@ Page Language="C#" AutoEventWireup="false" CodeBehind="Search.aspx.cs" Inherits="Search_Generative_Experience.Search" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" dir="rtl" lang="ar">
<head runat="server">
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>البحث القانوني الذكي</title>

  <!-- Roboto & Material Icons & Bootstrap RTL -->
  <link href="https://fonts.googleapis.com/css2?family=Roboto:wght@300;400;500&display=swap" rel="stylesheet" />
  <link href="https://fonts.googleapis.com/icon?family=Material+Icons" rel="stylesheet" />
  <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.rtl.min.css" rel="stylesheet" />
  <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js"></script>

  <style>
    * { box-sizing:border-box; margin:0; padding:0; }
    body {
      font-family:'Roboto',sans-serif;
      background:#f5f5f5;
      color:#202124;
      display:flex; align-items:center; justify-content:center;
      min-height:100vh; padding:20px;
    }
    .card {
      background:#fff; width:80vw; border-radius:12px;
      box-shadow:0 2px 8px rgba(0,0,0,0.1); padding:32px;
    }
    h1 {
      font-size:24px; font-weight:500; color:#1a73e8;
      margin-bottom:24px; text-align:center;
    }
    .search-bar {
      display:flex; gap:8px; margin-bottom:24px;
    }
    .search-bar input {
      flex:1; padding:14px 16px; border:1px solid #dadce0;
      border-radius:8px; font-size:16px;
    }
    .search-bar button {
      display:flex; align-items:center; gap:4px;
      background:#1a73e8; color:#fff; border:none;
      border-radius:8px; padding:0 16px; font-size:16px;
      cursor:pointer;
    }
    .search-bar button:hover { background:#1667c1; }

    .ai-box, .results {
      background:#fafafa; border-radius:8px; padding:20px;
      margin-top:16px; border-left:4px solid #1a73e8;
    }
    .results { border-left-color:#34a853; }

    .results ul {
      list-style:none; margin-top:8px; padding:0;
    }
    .results li {
      padding:12px 16px; background:#fff;
      border:1px solid #e0e0e0; border-radius:6px;
      margin-bottom:8px; cursor:pointer;
    }
    .results li:hover { background:#f1f3f4; }

    .loading { font-style:italic; color:#5f6368; }

    .truncate-3-lines {
      display:-webkit-box; -webkit-line-clamp:3;
      -webkit-box-orient:vertical; overflow:hidden;
      text-overflow:ellipsis;
    }

    /* Force Roboto inside modal */
    .modal-content, .modal-header, .modal-body, .modal-footer, .modal-title {
      font-family:'Roboto',sans-serif;
    }
    .modal-body pre { font-family:'Roboto',sans-serif; }
  </style>
</head>
<body>
  <form id="form1" runat="server">
    <div class="card">
      <h1>
        <span class="material-icons" style="vertical-align:middle;">gavel</span>
        البحث القانوني الذكي
      </h1>

      <div class="search-bar">
        <input type="text" id="txtQuery" placeholder="اكتب سؤالك هنا..." />
        <button type="button" onclick="performSearch()">
          <span class="material-icons">search</span> ابحث
        </button>
      </div>

      <div id="summaryDiv" class="ai-box" style="display:none;"></div>
      <div id="resultsDiv" class="results" style="display:none;"></div>
    </div>
  </form>

  <!-- Modal for full file -->
  <div class="modal fade" id="fileModal" tabindex="-1" aria-labelledby="fileModalLabel" aria-hidden="true">
    <div class="modal-dialog modal-lg modal-dialog-scrollable">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title" id="fileModalLabel">المحتوى الكامل</h5>
          <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="إغلاق"></button>
        </div>
        <div class="modal-body" id="fileModalBody">
          <p>جاري التحميل...</p>
        </div>
      </div>
    </div>
  </div>

  <script>
      async function performSearch() {
          const q = txtQuery.value.trim();
          const resultsDiv = document.getElementById('resultsDiv');
          const summaryDiv = document.getElementById('summaryDiv');
          resultsDiv.style.display = summaryDiv.style.display = 'block';
          resultsDiv.innerHTML = '<p class="loading">جاري تحميل النتائج...</p>';
          summaryDiv.innerHTML = '<p class="loading">جاري توليد الملخص...</p>';

          if (!q) {
              resultsDiv.innerHTML = '<p class="loading">يرجى إدخال نص للبحث.</p>';
              summaryDiv.style.display = 'none';
              return;
          }

          // 1) fetch results
          const data = await fetch(`SearchHandler.ashx?action=results&query=${encodeURIComponent(q)}`)
              .then(r => r.json())
              .catch(() => null);

          if (!data) {
              resultsDiv.innerHTML = '<p>حدث خطأ أثناء جلب النتائج.</p>';
          } else if (!data.length) {
              resultsDiv.innerHTML = '<p>لا توجد نتائج.</p>';
          } else {
              const ul = document.createElement('ul');
              data.forEach(item => {
                  const li = document.createElement('li');
                  li.innerHTML = `
            <strong>${item.Title}</strong><br>
            <div class="truncate-3-lines">${item.Content}</div>
          `;
                  li.addEventListener('click', () => {
                      const modalBody = document.getElementById('fileModalBody');
                      modalBody.innerHTML = '';

                      const iframe = document.createElement('iframe');
                      iframe.src = item.Url; // already includes #Anchor
                      iframe.style.width = '100%';
                      iframe.style.height = '80vh';
                      iframe.style.border = 'none';
                      iframe.title = 'Section Content';

                      iframe.onload = () => {
                          setTimeout(() => {
                              try {
                                  const doc = iframe.contentDocument || iframe.contentWindow.document;
                                  const el = doc.getElementById(item.Anchor) || doc.getElementsByName(item.Anchor)[0];
                                  if (el && typeof el.scrollIntoView === 'function') {
                                      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
                                  } else {
                                      console.warn('Anchor not found:', item.Anchor);
                                  }
                              } catch (e) {
                                  console.warn('Scroll error:', e);
                              }
                          }, 300);
                      };

                      modalBody.appendChild(iframe);
                      new bootstrap.Modal(document.getElementById('fileModal')).show();
                  });


                  ul.appendChild(li);
              });
              resultsDiv.innerHTML = '';
              resultsDiv.appendChild(ul);
          }

          // 2) stream summary (unchanged)
          try {
              const resp = await fetch(`SearchHandler.ashx?action=summary&query=${encodeURIComponent(q)}`);
              if (!resp.ok) throw new Error(resp.statusText);
              const reader = resp.body.getReader();
              const dec = new TextDecoder('utf-8');
              summaryDiv.innerHTML = '';
              while (true) {
                  const { done, value } = await reader.read();
                  if (done) break;
                  summaryDiv.innerHTML += dec.decode(value, { stream: true }).replace(/\n/g, '<br>');
              }
          } catch (err) {
              summaryDiv.innerHTML = `<p>خطأ في توليد الملخص: ${err.message}</p>`;
          }
      }
  </script>
</body>
</html>
