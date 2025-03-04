let inSearch = 0;

// SEARCH
async function SearchTrip() {
    if (isFetching) return; // Проверка, чтобы избежать дублирующих запросов

    isFetching = true; // Устанавливаем флаг в true, чтобы избежать параллельных запросов

    let rowsPerPage = localStorage.getItem('rowsPerPage') || 15;
    let searchByPerson = document.getElementById('search-person').value;
    let searchByPlace = document.getElementById('search-place').value;
    let searchByDates = document.getElementById('search-by-dates').value;
    let text_input = document.getElementById("search").value;
    let additional_text = "";

    if (text_input === "" && searchByDates === "" && searchByPerson === "Все" && searchByPlace === "Все") {
        inSearch = 0;
        location.reload();
    }

    if (searchByPerson !== "Все") {
        additional_text += `&person=${searchByPerson}`;
    }
    if (searchByPlace !== "Все") {
        additional_text += `&place=${searchByPlace}`;
    }
    if (searchByDates !== "") {
        additional_text += `&month=${searchByDates}`;
    }

    if (inSearch === 0) {
        inSearch = 1;
        currentPage = 1;
        updatePagination();
    }

    try {
        let response = await fetch(`/?handler=Search&text=${text_input}&page=${currentPage}&rows=${rowsPerPage}${additional_text}`);
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

function handleSearchEnter(event) {
    if (event.key === "Enter") {
        SearchTrip();
    }
}

function toggleClearButton() {
    const searchInput = document.getElementById("search");
    const clearBtn = document.getElementById("clear-btn");
    clearBtn.style.display = searchInput.value ? "block" : "none";
}

function clearSearch() {
    inSearch = 0;
    location.reload();
}


