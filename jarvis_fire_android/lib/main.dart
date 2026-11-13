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
    theme: ThemeData(colorSchemeSeed: Colors.red, useMaterial3: true),
    home: const DemoLoginPage(),
  );
}


const demoPasswordSha256 = '5e730980a8eef004aa65f3b8309f9ccd623abbcbd429b53b87b24bd6bb491c58';
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
        builder: (_) => const WaterMapPage()));
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
  const WaterMapPage({super.key});
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('ДЖАРВИС FIRE · РТП')),
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
