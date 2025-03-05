let currentPage = 1;
let totalPages = 1;
let rowsPerPage = localStorage.getItem('rowsPerPage') || 15;
let isFetching = false; // Флаг для предотвращения дублирующихся запросов

document.addEventListener("DOMContentLoaded", function () {
    document.getElementById("rows-per-page").value = rowsPerPage;

    document.getElementById("first-btn").addEventListener("click", () => changePage('first',1));
    document.getElementById("prev-btn").addEventListener("click", () => changePage('prev',1));
    document.getElementById("next-btn").addEventListener("click", () => changePage('next',1));
    document.getElementById("last-btn").addEventListener("click", () => changePage('last',1));

    // При первой загрузке страницы делаем запрос
    fetchTableData(currentPage, rowsPerPage);
});
function changePage(action,btn) {
    // Если запрос в процессе, не выполняем изменения страницы
    if (isFetching) return;

    if (btn === 1) {
        let btnId = {
            'first': "first-btn",
            'prev': "prev-btn",
            'next': "next-btn",
            'last': "last-btn"
        }[action];
        if (document.getElementById(btnId)?.disabled) return;
    }

    // Обработка изменения текущей страницы
    if (action === 'first') {
        currentPage = 1;
    } else if (action === 'prev' && currentPage > 1) {
        currentPage--;
    } else if (action === 'next' && currentPage < totalPages) {
        currentPage++;
    } else if (action === 'last') {
        currentPage = totalPages;
    }

    if (inSearch === 1) {
        SearchZone("");
        return;
    }

    // Обновить активность кнопок
    updatePagination();

    // Обновить таблицу после изменения страницы
    fetchTableData(currentPage, rowsPerPage);
}

function changeRowsPerPage() {
    // Если запрос в процессе, не выполняем изменения
    if (isFetching) return;

    // Изменить количество строк на странице
    rowsPerPage = document.getElementById("rows-per-page").value;
    localStorage.setItem("rowsPerPage", rowsPerPage);
    currentPage = 1; // Сброс на первую страницу

    // Обновить таблицу после изменения количества строк на странице
    changePage("first",0);
}

function updatePagination() {
    // Обновить номер страницы на интерфейсе
    document.getElementById("page-number").innerText = currentPage;

    setButtonState("prev-btn", currentPage > 1);
    setButtonState("first-btn", currentPage > 1);
    setButtonState("next-btn", currentPage < totalPages);
    setButtonState("last-btn", currentPage < totalPages);
}

function setButtonState(buttonId, isActive) {
    let button = document.getElementById(buttonId);

    if (!button) return;

    button.disabled = !isActive; // Блокируем кнопку
    button.classList.toggle('disabled', !isActive); // Добавляем/убираем визуальный стиль
}


async function fetchTableData(page, rows) {
    if (isFetching) return; // Проверка, чтобы избежать дублирующих запросов

    isFetching = true; // Устанавливаем флаг в true, чтобы избежать параллельных запросов

    let baseUrl = window.location.pathname;
    let handler = `${baseUrl}?handler=GetTableData&page=${page}&rows=${rows}`;

    try {
        let response = await fetch(handler);
        if (response.ok) {
            const url = window.location.href.split('?')[0];
            window.history.replaceState({}, '', url);

            let data = await response.json();

            if (data.success) {
                renderTable(data.items);
                totalPages = data.totalPages;
                updatePagination();
            } else {
                showNotification('Error: ' + data.message, 2);
            }
        } else {
            showNotification('Error: ' + response.message, 2);
            console.error("Ошибка загрузки данных: " + response.message);
        }
    } catch (error) {
        showNotification('Error: ' + error, 2);
        console.error("Ошибка запроса:", error);
    } finally {
        isFetching = false;
    }
}
