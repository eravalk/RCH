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
    home: const WaterMapPage(),
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
