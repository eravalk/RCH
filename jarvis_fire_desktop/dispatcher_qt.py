"""ДЖАРВИС FIRE — desktop dispatcher training shell (not for operational use)."""
import hashlib
import hmac
import json
import os
import sqlite3
import sys
from datetime import datetime
from pathlib import Path

from PySide6.QtCore import Qt, QUrl
from PySide6.QtWidgets import (
    QApplication, QComboBox, QDialog, QFormLayout, QFrame, QHBoxLayout,
    QHeaderView, QLabel, QLineEdit, QMainWindow, QMessageBox, QPushButton,
    QSplitter, QStackedWidget, QTableWidget, QTableWidgetItem, QTextEdit,
    QVBoxLayout, QWidget, QFileDialog
)
from PySide6.QtGui import QDesktopServices
try:
    from PySide6.QtWebEngineWidgets import QWebEngineView
except ImportError:
    QWebEngineView = None

HASH = "5e730980a8eef004aa65f3b8309f9ccd623abbcbd429b53b87b24bd6bb491c58"
HOME = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "JarvisFireTraining"
HOME.mkdir(parents=True, exist_ok=True)
DB = HOME / "dispatcher.sqlite3"
WARNING = "УЧЕБНЫЙ ПРОТОТИП • НЕ ИСПОЛЬЗОВАТЬ ДЛЯ РЕАЛЬНЫХ ВЫЗОВОВ"
RED = "#e84d57"
CSS = """
QWidget {background:#111923;color:#e6edf5;font-family:'Segoe UI';font-size:12px}
QMainWindow {background:#0b111a}
QFrame#side {background:#0c1420;border-right:1px solid #253344}
QFrame#card {background:#192635;border:1px solid #2b3d50;border-radius:10px}
QLabel#title {font-size:23px;font-weight:700}
QLabel#muted {color:#9aafc3}
QLabel#warning {color:#ffb75d;font-weight:700}
QPushButton {background:#253449;border:1px solid #33475d;border-radius:7px;padding:10px;text-align:left}
QPushButton:hover {background:#34465c}
QPushButton#primary {background:#c63f4a;color:white;font-weight:700}
QLineEdit,QComboBox,QTextEdit {background:#152333;border:1px solid #34485d;border-radius:6px;padding:9px}
QTableWidget {background:#162232;alternate-background-color:#1d2c3c;gridline-color:#304253;border:1px solid #304253}
QHeaderView::section {background:#26374a;padding:9px;border:0}
"""
MAP_HTML = """<!doctype html><html><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<style>html,body,#map{height:100%;margin:0;background:#182838} .badge{position:absolute;top:12px;left:50px;transform:translateX(-50%);z-index:1000;background:#302b25;color:#ffcb76;padding:8px;border-radius:8px;font:12px Arial}</style></head>
<body><div id="map"></div><div class="badge">УЧЕБНАЯ КАРТА • ТОЧКИ ВЫМЫШЛЕНЫ</div>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script><script>
if(window.L){let m=L.map('map').setView([45.0445,41.969],12);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{attribution:'© OpenStreetMap contributors',maxZoom:19}).addTo(m);
[[45.0433,41.9690,'Учебный гидрант №1'],[45.0461,41.9728,'Учебный гидрант №2'],[45.0407,41.9652,'Учебный гидрант №3']].forEach(x=>L.circleMarker([x[0],x[1]],{radius:9,color:'#ff4545',fillColor:'#ff4545',fillOpacity:.9}).addTo(m).bindPopup(x[2]+'<br>Демонстрационные данные'));
}else{document.getElementById('map').innerHTML='<p style="color:white;padding:40px">Для карты требуется подключение к интернету.</p>'}
</script></body></html>"""

def database():
    conn = sqlite3.connect(DB)
    conn.execute("CREATE TABLE IF NOT EXISTS calls (id INTEGER PRIMARY KEY, time TEXT, address TEXT, kind TEXT, description TEXT, status TEXT)")
    conn.execute("CREATE TABLE IF NOT EXISTS history (id INTEGER PRIMARY KEY, call_id INTEGER, time TEXT, event TEXT)")
    conn.execute("CREATE TABLE IF NOT EXISTS objects (id INTEGER PRIMARY KEY, name TEXT, address TEXT, notes TEXT)")
    conn.execute("CREATE TABLE IF NOT EXISTS water (id INTEGER PRIMARY KEY, name TEXT, lat REAL, lon REAL, notes TEXT)")
    if not conn.execute("SELECT 1 FROM water LIMIT 1").fetchone():
        conn.executemany("INSERT INTO water(name,lat,lon,notes) VALUES (?,?,?,?)", [
            ("Учебный гидрант №1",45.0433,41.969,"Вымышленная точка"),
            ("Учебный гидрант №2",45.0461,41.9728,"Вымышленная точка"),
            ("Учебный гидрант №3",45.0407,41.9652,"Вымышленная точка")])
    conn.commit()
    return conn

class Login(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("ДЖАРВИС FIRE — авторизация")
        self.resize(420,330)
        box=QVBoxLayout(self)
        heading=QLabel("🚒  ДЖАРВИС FIRE")
        heading.setObjectName("title")
        box.addWidget(heading)
        box.addWidget(QLabel("Рабочее место диспетчера · учебный режим"))
        self.user=QLineEdit()
        self.user.setPlaceholderText("Логин")
        self.password=QLineEdit()
        self.password.setPlaceholderText("Пароль")
        self.password.setEchoMode(QLineEdit.EchoMode.Password)
        box.addWidget(self.user)
        box.addWidget(self.password)
        btn=QPushButton("ВОЙТИ")
        btn.setObjectName("primary")
        btn.clicked.connect(self.check)
        self.password.returnPressed.connect(self.check)
        box.addWidget(btn)
        notice=QLabel(WARNING)
        notice.setObjectName("warning")
        notice.setWordWrap(True)
        box.addWidget(notice)
    def check(self):
        digest=hashlib.sha256(self.password.text().encode()).hexdigest()
        if self.user.text().strip()=="admin" and hmac.compare_digest(digest,HASH):
            self.accept()
        else:
            QMessageBox.warning(self,"Авторизация","Неверный логин или пароль")

class Dispatcher(QMainWindow):
    def __init__(self):
        super().__init__()
        self.conn=database()
        self.setWindowTitle("ДЖАРВИС FIRE | Диспетчерская • учебный режим")
        self.resize(1450,860)
        root=QWidget()
        self.setCentralWidget(root)
        row=QHBoxLayout(root)
        row.setContentsMargins(0,0,0,0)
        row.setSpacing(0)
        sidebar=QFrame()
        sidebar.setObjectName("side")
        sidebar.setFixedWidth(235)
        nav=QVBoxLayout(sidebar)
        brand=QLabel("🚒  ДЖАРВИС FIRE")
        brand.setObjectName("title")
        nav.addWidget(brand)
        nav.addWidget(QLabel("ДИСПЕТЧЕРСКАЯ • TRAINING"))
        nav.addSpacing(25)
        self.pages=QStackedWidget()
        for i,(name,creator) in enumerate([
            ("Пульт диспетчера",self.dashboard),
            ("Вызовы и журнал",self.calls_page),
            ("Карта Ставрополя",self.map_page),
            ("Водоисточники",self.water_page),
            ("Объекты",self.objects_page),
            ("Архив",self.archive_page),
            ("Документы",self.documents_page),
            ("Пользователи",self.users_page),
            ("Настройки",self.settings_page)]):
            b=QPushButton(name)
            b.clicked.connect(lambda checked=False,idx=i:self.navigate(idx))
            nav.addWidget(b)
            self.pages.addWidget(creator())
        nav.addStretch()
        nav.addWidget(QLabel("АДМИНИСТРАТОР · ЛОКАЛЬНЫЙ РЕЖИМ"))
        row.addWidget(sidebar)
        row.addWidget(self.pages,1)
        self.navigate(0)

    def navigate(self,index):
        self.pages.setCurrentIndex(index)
        if index in (0,1,5):
            self.refresh_calls()
    def page(self,title,subtitle=""):
        w=QWidget()
        layout=QVBoxLayout(w)
        layout.setContentsMargins(25,22,25,22)
        h=QLabel(title)
        h.setObjectName("title")
        layout.addWidget(h)
        if subtitle:
            sub=QLabel(subtitle)
            sub.setObjectName("muted")
            sub.setWordWrap(True)
            layout.addWidget(sub)
        notice=QLabel(WARNING)
        notice.setObjectName("warning")
        layout.addWidget(notice)
        return w,layout
    def table(self,columns):
        t=QTableWidget(0,len(columns))
        t.setHorizontalHeaderLabels(columns)
        t.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        t.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        t.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        t.setAlternatingRowColors(True)
        return t
    def fill(self,t,rows):
        t.setRowCount(0)
        for row in rows:
            n=t.rowCount()
            t.insertRow(n)
            for j,value in enumerate(row):
                t.setItem(n,j,QTableWidgetItem(str(value)))
    def dashboard(self):
        w,l=self.page("Пульт диспетчера","Оперативная сводка, активные вызовы и карта района")
        stats=QHBoxLayout()
        self.count_label=QLabel("Активные вызовы: 0")
        self.count_label.setObjectName("title")
        stats.addWidget(self.count_label)
        stats.addStretch()
        new=QPushButton("+ НОВЫЙ ВЫЗОВ")
        new.setObjectName("primary")
        new.clicked.connect(self.new_call)
        stats.addWidget(new)
        l.addLayout(stats)
        splitter=QSplitter(Qt.Orientation.Horizontal)
        self.dashboard_table=self.table(["№","Время","Адрес","Тип","Статус"])
        splitter.addWidget(self.dashboard_table)
        splitter.addWidget(self.map_widget())
        splitter.setSizes([520,700])
        l.addWidget(splitter,1)
        return w
    def calls_page(self):
        w,l=self.page("Вызовы и журнал событий","Регистрация, сопровождение и закрытие учебных происшествий")
        actions=QHBoxLayout()
        for name,fn in [("+ Создать вызов",self.new_call),("Принять",lambda:self.status("Принят")),("Выезд",lambda:self.status("В пути")),("На месте",lambda:self.status("На месте")),("Закрыть",lambda:self.status("Закрыт"))]:
            b=QPushButton(name)
            b.clicked.connect(fn)
            actions.addWidget(b)
        l.addLayout(actions)
        self.calls_table=self.table(["№","Время","Адрес","Тип","Описание","Статус"])
        l.addWidget(self.calls_table,1)
        self.log_table=self.table(["Время","Вызов №","Событие"])
        l.addWidget(QLabel("ХРОНОЛОГИЯ СОБЫТИЙ"))
        l.addWidget(self.log_table,1)
        return w
    def map_widget(self):
        if QWebEngineView:
            view=QWebEngineView()
            view.setHtml(MAP_HTML,QUrl("https://unpkg.com/"))
            return view
        label=QLabel("Карта недоступна: модуль QtWebEngine не установлен.")
        label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        return label
    def map_page(self):
        w,l=self.page("Карта Ставрополя и Ставропольского края","OpenStreetMap • демонстрационные водоисточники • требуется интернет")
        l.addWidget(self.map_widget(),1)
        return w
    def water_page(self):
        w,l=self.page("Водоисточники","Реестр учебных гидрантов и координат; реальные данные не загружены")
        t=self.table(["Название","Широта","Долгота","Примечание"])
        rows=self.conn.execute("SELECT name,lat,lon,notes FROM water").fetchall()
        self.fill(t,rows)
        l.addWidget(t)
        return w
    def objects_page(self):
        w,l=self.page("Объекты","Реестр объектов; заполнение и серверная синхронизация в разработке")
        t=self.table(["Название","Адрес","Примечание"])
        self.fill(t,self.conn.execute("SELECT name,address,notes FROM objects").fetchall())
        l.addWidget(t)
        return w
    def archive_page(self):
        w,l=self.page("Архив вызовов","Закрытые учебные вызовы")
        self.archive_table=self.table(["№","Время","Адрес","Тип","Статус"])
        l.addWidget(self.archive_table)
        return w
    def documents_page(self):
        w,l=self.page("Документы","Формирование Word-документов предусмотрено ТЗ, но пока не реализовано")
        l.addWidget(QLabel("Экспорт журнала в CSV доступен в настройках."))
        l.addStretch()
        return w
    def users_page(self):
        w,l=self.page("Пользователи и роли","Серверное управление учётными записями ещё не подключено")
        l.addWidget(QLabel("Тестовая учётная запись: admin · локальный доступ"))
        l.addStretch()
        return w
    def settings_page(self):
        w,l=self.page("Настройки и диагностика","Локальная учебная версия, без подключения к серверу")
        l.addWidget(QLabel("База данных: "+str(DB)))
        l.addWidget(QLabel("Сервер: не подключён"))
        l.addWidget(QLabel("Синхронизация Android: не подключена"))
        l.addWidget(QLabel("Голосовой помощник: не подключён"))
        btn=QPushButton("Экспорт журнала в CSV")
        btn.clicked.connect(self.export_csv)
        l.addWidget(btn)
        l.addStretch()
        return w
    def new_call(self):
        dialog=QDialog(self)
        dialog.setWindowTitle("Новый учебный вызов")
        dialog.resize(510,380)
        form=QFormLayout(dialog)
        address=QLineEdit()
        kind=QComboBox()
        kind.addItems(["Пожар","Задымление","ДТП","Другое"])
        description=QTextEdit()
        form.addRow("Адрес:",address)
        form.addRow("Категория:",kind)
        form.addRow("Описание:",description)
        save=QPushButton("ЗАРЕГИСТРИРОВАТЬ")
        save.setObjectName("primary")
        form.addRow(save)
        def submit():
            if not address.text().strip() or not description.toPlainText().strip():
                QMessageBox.warning(dialog,"Поля","Укажите адрес и описание")
                return
            now=datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            cur=self.conn.execute("INSERT INTO calls(time,address,kind,description,status) VALUES (?,?,?,?,?)",
                (now,address.text().strip(),kind.currentText(),description.toPlainText().strip(),"Новый"))
            self.conn.execute("INSERT INTO history(call_id,time,event) VALUES (?,?,?)",
                (cur.lastrowid,now,"Создан учебный вызов"))
            self.conn.commit()
            dialog.accept()
            self.refresh_calls()
        save.clicked.connect(submit)
        dialog.exec()
    def status(self,value):
        row=self.calls_table.currentRow()
        if row<0:
            QMessageBox.information(self,"Выбор","Выберите вызов в таблице")
            return
        call_id=int(self.calls_table.item(row,0).text())
        now=datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        self.conn.execute("UPDATE calls SET status=? WHERE id=?",(value,call_id))
        self.conn.execute("INSERT INTO history(call_id,time,event) VALUES (?,?,?)",
            (call_id,now,"Статус: "+value))
        self.conn.commit()
        self.refresh_calls()
    def refresh_calls(self):
        active=self.conn.execute("SELECT id,time,address,kind,status FROM calls WHERE status!='Закрыт' ORDER BY id DESC").fetchall()
        self.count_label.setText("Активные вызовы: "+str(len(active)))
        self.fill(self.dashboard_table,active)
        self.fill(self.calls_table,self.conn.execute(
            "SELECT id,time,address,kind,description,status FROM calls ORDER BY id DESC").fetchall())
        self.fill(self.archive_table,self.conn.execute(
            "SELECT id,time,address,kind,status FROM calls WHERE status='Закрыт' ORDER BY id DESC").fetchall())
        self.fill(self.log_table,self.conn.execute(
            "SELECT time,call_id,event FROM history ORDER BY id DESC LIMIT 100").fetchall())
    def export_csv(self):
        import csv
        path,_=QFileDialog.getSaveFileName(self,"Экспорт журнала","jarvis_calls.csv","CSV (*.csv)")
        if not path:
            return
        with open(path,"w",newline="",encoding="utf-8-sig") as f:
            writer=csv.writer(f,delimiter=";")
            writer.writerow(["№","Время","Адрес","Категория","Описание","Статус"])
            writer.writerows(self.conn.execute("SELECT id,time,address,kind,description,status FROM calls"))
        QMessageBox.information(self,"Экспорт","Файл сохранён")
    def closeEvent(self,event):
        self.conn.close()
        super().closeEvent(event)

if __name__=="__main__":
    app=QApplication(sys.argv)
    app.setStyleSheet(CSS)
    if Login().exec()==QDialog.DialogCode.Accepted:
        window=Dispatcher()
        window.show()
        sys.exit(app.exec())
