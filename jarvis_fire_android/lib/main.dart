import 'dart:convert';
import 'package:crypto/crypto.dart';
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

void main() => runApp(const JarvisFireApp());

class WaterSource {
  final String name;
  final LatLng location;
  const WaterSource(this.name, this.location);
}

const sources = [
  WaterSource('Учебный гидрант №1', LatLng(45.0433, 41.9690)),
  WaterSource('Учебный гидрант №2', LatLng(45.0461, 41.9728)),
  WaterSource('Учебный гидрант №3', LatLng(45.0407, 41.9652)),
];

class JarvisFireApp extends StatelessWidget {
  const JarvisFireApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'ДЖАРВИС FIRE · РТП',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(brightness: Brightness.dark, scaffoldBackgroundColor: const Color(0xFF081523), colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFFDA303B), brightness: Brightness.dark), useMaterial3: true),
    home: const DemoLoginPage(),
  );
}


const demoPasswordSha256 = '5994471abb01112afcc18159f6cc74b4f511b99806da59b3caf5a9c173cacfc5';
class DemoLoginPage extends StatefulWidget {
  const DemoLoginPage({super.key});
  @override
  State<DemoLoginPage> createState() => _DemoLoginPageState();
}
class _DemoLoginPageState extends State<DemoLoginPage> {
  final username = TextEditingController();
  final password = TextEditingController();
  bool hidden = true;
  String? error;
  @override
  void dispose() {
    username.dispose();
    password.dispose();
    super.dispose();
  }
  void submit() {
    if (username.text.trim() == 'admin' &&
        sha256.convert(utf8.encode(password.text)).toString() == demoPasswordSha256) {
      Navigator.of(context).pushReplacement(MaterialPageRoute<void>(
        builder: (_) => const CommanderHome()));
    } else {
      setState(() => error = 'Неверный логин или пароль');
    }
  }
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('ДЖАРВИС FIRE · Вход')),
    body: Center(child: SingleChildScrollView(child: ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 420),
      child: Padding(padding: const EdgeInsets.all(24), child: Column(
        mainAxisSize: MainAxisSize.min, children: [
          const Icon(Icons.local_fire_department, size: 64, color: Colors.red),
          const SizedBox(height: 16),
          Text('Учебный вход администратора', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 20),
          TextField(controller: username, decoration: const InputDecoration(
            labelText: 'Логин', border: OutlineInputBorder())),
          const SizedBox(height: 12),
          TextField(controller: password, obscureText: hidden, onSubmitted: (_) => submit(),
            decoration: InputDecoration(labelText: 'Пароль', border: const OutlineInputBorder(),
              suffixIcon: IconButton(
                icon: Icon(hidden ? Icons.visibility : Icons.visibility_off),
                onPressed: () => setState(() => hidden = !hidden)))),
          if (error != null) Padding(padding: const EdgeInsets.all(12),
            child: Text(error!, style: const TextStyle(color: Colors.red))),
          const SizedBox(height: 16),
          FilledButton(onPressed: submit, child: const Text('Войти')),
          const SizedBox(height: 12),
          const Text('Только для тестирования. Не использовать для реальных вызовов.'),
        ],
      )),
    ))),
  );
}
class WaterMapPage extends StatelessWidget {
  final bool embedded;
  const WaterMapPage({super.key, this.embedded = false});
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: embedded ? null : AppBar(title: const Text('ДЖАРВИС FIRE · РТП')),
    body: Column(children: [
      const MaterialBanner(
        content: Text('УЧЕБНЫЙ ПРОТОТИП. Точки вымышлены. Не использовать при реальных вызовах.'),
        actions: [SizedBox.shrink()],
      ),
      Expanded(child: FlutterMap(
        options: const MapOptions(initialCenter: LatLng(45.0445, 41.9690), initialZoom: 14),
        children: [
          TileLayer(
            urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            userAgentPackageName: 'ru.jarvisfire.training',
          ),
          MarkerLayer(markers: [
            for (final source in sources)
              Marker(
                point: source.location,
                width: 42, height: 42,
                child: IconButton(
                  tooltip: source.name,
                  icon: const Icon(Icons.local_fire_department, color: Colors.red, size: 26),
                  onPressed: () => showModalBottomSheet<void>(
                    context: context,
                    builder: (context) => SafeArea(child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Text('${source.name}\n${source.location.latitude}, ${source.location.longitude}\nДемонстрационные данные',
                        style: Theme.of(context).textTheme.titleMedium),
                    )),
                  ),
                ),
              ),
          ]),
          const RichAttributionWidget(attributions: [
            TextSourceAttribution('OpenStreetMap contributors'),
          ]),
        ],
      )),
    ]),
  );
}

class CommanderHome extends StatefulWidget {
  const CommanderHome({super.key});
  @override
  State<CommanderHome> createState() => _CommanderHomeState();
}

class _CommanderHomeState extends State<CommanderHome> {
  int tab = 0;
  String stage = 'Новый';
  final stages = <String>['Принять вызов', 'Следую к месту', 'На месте', 'Локализация', 'Ликвидация'];
  final names = <String>['ВЫЗОВ', 'ОБЪЕКТ', 'КАРТА', 'ПОМОЩНИК', 'ЖУРНАЛ', 'ФОТОГРАФИИ'];
  final icons = <IconData>[Icons.local_fire_department, Icons.apartment, Icons.map, Icons.mic, Icons.list_alt, Icons.photo_camera];
  final events = <String>[];
  Widget box(Widget child) => Container(
    margin: const EdgeInsets.only(bottom: 12),
    padding: const EdgeInsets.all(15),
    decoration: BoxDecoration(color: const Color(0xFF13263A), border: Border.all(color: const Color(0xFF294158)), borderRadius: BorderRadius.circular(12)),
    child: child,
  );
  void updateStage(String value) {
    setState(() {
      stage = value;
      events.insert(0, '${TimeOfDay.now().format(context)} — ${value}');
    });
    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Статус сохранён локально. Серверная синхронизация не подключена.')));
  }
  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(backgroundColor: const Color(0xFF0B1B2C),
        title: const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('🚒 ДЖАРВИС FIRE', style: TextStyle(fontSize: 19, fontWeight: FontWeight.bold)),
          Text('Мобильный РТП · учебный режим', style: TextStyle(fontSize: 11)),
        ]),
        actions: const [Padding(padding: EdgeInsets.all(15), child: Icon(Icons.circle, color: Colors.green, size: 12))]),
      body: Column(children: [
        Container(width: double.infinity, color: const Color(0xFF4D252C),
          padding: const EdgeInsets.all(10),
          child: const Text('УЧЕБНАЯ ДЕМОНСТРАЦИЯ • НЕТ ПОДКЛЮЧЕНИЯ К ДИСПЕТЧЕРСКОЙ',
            textAlign: TextAlign.center, style: TextStyle(color: Color(0xFFFFD18A), fontSize: 11, fontWeight: FontWeight.bold))),
        Expanded(child: IndexedStack(index: tab, children: [
          SingleChildScrollView(padding: const EdgeInsets.all(14), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            box(const Row(children: [Icon(Icons.local_fire_department, color: Colors.red, size: 32), SizedBox(width: 12),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('ВЫЗОВ № 1527', style: TextStyle(fontSize: 21, fontWeight: FontWeight.bold)),
                Text('Пожар • демонстрационный вызов'),
              ])), Chip(label: Text('Учебный'))])),
            box(const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('г. Ставрополь, ул. Ленина, д. 124', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
              SizedBox(height: 8), Text('Жилой дом • 5 этажей'),
              Text('Предварительно — квартира, 3 этаж'),
              SizedBox(height: 8), Text('Угроза людям: ЕСТЬ', style: TextStyle(color: Color(0xFFFF7777), fontWeight: FontWeight.bold)),
              SizedBox(height: 8), Text('АДРЕС И СВЕДЕНИЯ ВЫМЫШЛЕНЫ', style: TextStyle(fontSize: 11, color: Colors.amber)),
            ])),
            box(const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('ИНФОРМАЦИЯ', style: TextStyle(fontWeight: FontWeight.bold)),
              SizedBox(height: 10), Text('Время поступления: 10:17'),
              Text('Назначенные силы: ПСЧ-1, ПСЧ-3 (пример)'),
              Text('Связь с диспетчером: не подключена'),
            ])),
            const Padding(padding: EdgeInsets.only(bottom: 10), child: Text('СТАТУС РТП', style: TextStyle(fontWeight: FontWeight.bold))),
            for (var i=0; i<stages.length; i++) Padding(padding: const EdgeInsets.only(bottom: 8),
              child: FilledButton.icon(
                style: FilledButton.styleFrom(backgroundColor: i==0 ? const Color(0xFF168C46) : const Color(0xFF20384E),
                  minimumSize: const Size.fromHeight(47)),
                onPressed: () => updateStage(stages[i]),
                icon: Icon(i==0 ? Icons.check_circle : Icons.radio_button_checked),
                label: Text(stages[i]))),
            box(Text('Текущий локальный статус: ${stage}')),
            FilledButton.icon(
              style: FilledButton.styleFrom(backgroundColor: const Color(0xFFB7353F), minimumSize: const Size.fromHeight(50)),
              onPressed: () => ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Запрос поддержки требует подключения к серверу'))),
              icon: const Icon(Icons.sos), label: const Text('Запросить поддержку')),
          ])),
          SingleChildScrollView(padding: const EdgeInsets.all(15), child: box(const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('ОБЪЕКТ', style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold)),
            SizedBox(height: 12), Text('г. Ставрополь, ул. Ленина, д. 124'),
            Text('Учебный пример. Документы и поэтажные планы появятся после подключения реестра объектов.'),
          ]))),
          const WaterMapPage(embedded: true),
          Center(child: box(const Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(Icons.mic, size: 60), SizedBox(height: 15),
            Text('ДЖАРВИС • ПОМОЩНИК', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
            SizedBox(height: 10), Text('Голосовой модуль ещё не подключён.'),
          ]))),
          ListView(padding: const EdgeInsets.all(15), children: [
            const Text('ЛОКАЛЬНЫЙ ЖУРНАЛ СТАТУСОВ', style: TextStyle(fontWeight: FontWeight.bold)),
            if (events.isEmpty) const ListTile(title: Text('Событий пока нет')),
            for (final e in events) ListTile(leading: const Icon(Icons.history), title: Text(e)),
          ]),
          Center(child: box(const Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(Icons.photo_camera, size: 60), SizedBox(height: 15),
            Text('ФОТОГРАФИИ', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
            SizedBox(height: 10), Text('Загрузка фотографий на сервер будет добавлена после синхронизации.'),
          ]))),
        ])),
      ]),
      bottomNavigationBar: SafeArea(child: Container(
        color: const Color(0xFF0B1B2C), padding: const EdgeInsets.symmetric(vertical: 7, horizontal: 3),
        child: Row(children: [
          for (var i=0;i<names.length;i++) Expanded(child: InkWell(
            onTap: () => setState(() => tab=i),
            child: Padding(padding: const EdgeInsets.symmetric(vertical: 7),
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Icon(icons[i], color: tab==i ? Colors.redAccent : Colors.white70, size: 22),
                const SizedBox(height: 5),
                Text(names[i], textAlign: TextAlign.center,
                  style: TextStyle(fontSize: 8, color: tab==i ? Colors.redAccent : Colors.white70)),
              ])))),
        ]),
      )),
    );
  }
}
