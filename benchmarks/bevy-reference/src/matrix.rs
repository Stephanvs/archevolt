//! Bevy counterparts of Archevolt's `TraversalMatrixBenchmarks` and `FilterBenchmarks`, with the
//! components and population of `MatrixWorkloads.cs`.

use bevy_ecs::prelude::*;

use crate::{DELTA, Velocity, particle, single_threaded};

#[derive(Component)]
pub struct Tag0;
#[derive(Component)]
pub struct Tag1;
#[derive(Component)]
pub struct Tag2;
#[derive(Component)]
pub struct Tag3;

/// 256 bytes, like Archevolt's `[StructLayout(Size = 256)] WidePayload`.
#[derive(Component, Clone, Copy)]
#[repr(C)]
pub struct WidePayload {
    value: i32,
    padding: [u8; 252],
}

impl WidePayload {
    pub fn new(value: i32) -> Self {
        Self {
            value,
            padding: [0; 252],
        }
    }
}

/// Bevy tracks changes for every component. Archevolt tracks this one through `[TrackChanges]`.
#[derive(Component, Clone, Copy, Debug, PartialEq)]
pub struct TrackedPosition {
    x: f32,
    y: f32,
    z: f32,
}

impl TrackedPosition {
    fn advanced(self, velocity: &Velocity) -> Self {
        Self {
            x: self.x + velocity.x * DELTA,
            y: self.y + velocity.y * DELTA,
            z: self.z + velocity.z * DELTA,
        }
    }
}

#[derive(Component)]
pub struct Acceleration(f32);

#[derive(Resource, Default, Debug, PartialEq)]
pub struct ScanTotal {
    count: u64,
    sum: f64,
}

/// Spawns `count` particles one at a time, then moves entity `i` into archetype `i % archetypes`
/// through tag inserts and adds the unread payload, in the order `MatrixPopulation.Populate` uses.
pub fn populate(count: usize, archetypes: usize, wide: bool) -> (World, Vec<Entity>) {
    let mut world = World::new();
    let mut entities = Vec::with_capacity(count);
    for i in 0..count {
        let mut entity = world.spawn(particle(i));
        let mask = i % archetypes;
        if mask & 1 != 0 {
            entity.insert(Tag0);
        }
        if mask & 2 != 0 {
            entity.insert(Tag1);
        }
        if mask & 4 != 0 {
            entity.insert(Tag2);
        }
        if mask & 8 != 0 {
            entity.insert(Tag3);
        }
        if wide {
            entity.insert(WidePayload::new(i as i32));
        }
        entities.push(entity.id());
    }
    (world, entities)
}

pub fn mark(mut query: Query<(&mut TrackedPosition, &Velocity)>) {
    query.iter_mut().for_each(|(mut position, velocity)| {
        *position = position.advanced(velocity);
    });
}

pub fn changed(
    query: Query<&TrackedPosition, Changed<TrackedPosition>>,
    mut total: ResMut<ScanTotal>,
) {
    let mut count = 0;
    let mut sum = 0.0;
    for position in &query {
        count += 1;
        sum += f64::from(position.x);
    }
    *total = ScanTotal { count, sum };
}

pub fn optional(
    query: Query<(&TrackedPosition, Option<&Acceleration>)>,
    mut total: ResMut<ScanTotal>,
) {
    let mut count = 0;
    let mut sum = 0.0;
    for (position, acceleration) in &query {
        count += 1;
        sum += f64::from(position.x);
        if let Some(acceleration) = acceleration {
            sum += f64::from(acceleration.0);
        }
    }
    *total = ScanTotal { count, sum };
}

/// The `FilterBenchmarks` world: tracked particles, `Acceleration` on every even entity, and one
/// schedule per system. Construction runs the scans once, so the changed scan has consumed the
/// spawn ticks.
pub struct Filters {
    world: World,
    writer: Schedule,
    changed: Schedule,
    optional: Schedule,
}

impl Filters {
    pub fn new(count: usize) -> Self {
        let mut world = World::new();
        world.insert_resource(ScanTotal::default());
        for i in 0..count {
            let (position, velocity) = particle(i);
            let tracked = TrackedPosition {
                x: position.x,
                y: position.y,
                z: position.z,
            };
            let mut entity = world.spawn((tracked, velocity));
            if i % 2 == 0 {
                entity.insert(Acceleration(0.25));
            }
        }
        let mut filters = Self {
            world,
            writer: single_threaded(mark),
            changed: single_threaded(changed),
            optional: single_threaded(optional),
        };
        filters.changed_none();
        filters.optional_half_present();
        filters
    }

    pub fn tracked_write(&mut self) {
        self.writer.run(&mut self.world);
    }

    pub fn changed_none(&mut self) {
        self.changed.run(&mut self.world);
    }

    pub fn write_then_changed_all(&mut self) {
        self.tracked_write();
        self.changed_none();
    }

    pub fn optional_half_present(&mut self) {
        self.optional.run(&mut self.world);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{Position, Scheduled, for_each};

    fn total(filters: &Filters) -> &ScanTotal {
        filters.world.resource::<ScanTotal>()
    }

    fn sorted_tracked_x(filters: &mut Filters) -> Vec<f32> {
        let mut query = filters.world.query::<&TrackedPosition>();
        let mut xs: Vec<f32> = query.iter(&filters.world).map(|p| p.x).collect();
        xs.sort_by(f32::total_cmp);
        xs
    }

    #[test]
    fn movement_advances_every_entity_across_archetypes_and_keeps_its_layout() {
        for archetypes in [1, 16] {
            for wide in [false, true] {
                let (world, entities) = populate(1_000, archetypes, wide);
                let mut scheduled = Scheduled::from_world(world, for_each);
                scheduled.run();
                let world = &scheduled.world;

                for (i, &entity) in entities.iter().enumerate() {
                    let (position, velocity) = particle(i);
                    let expected = position.advanced(&velocity, DELTA);
                    assert_eq!(world.get::<Position>(entity), Some(&expected));
                    let mask = i % archetypes;
                    assert_eq!(world.get::<Tag0>(entity).is_some(), mask & 1 != 0);
                    assert_eq!(world.get::<Tag1>(entity).is_some(), mask & 2 != 0);
                    assert_eq!(world.get::<Tag2>(entity).is_some(), mask & 4 != 0);
                    assert_eq!(world.get::<Tag3>(entity).is_some(), mask & 8 != 0);
                    let payload = world
                        .get::<WidePayload>(entity)
                        .map(|payload| payload.value);
                    assert_eq!(payload, wide.then_some(i as i32));
                }
            }
        }
    }

    #[test]
    fn changed_scan_finds_nothing_until_the_tracked_write() {
        let mut filters = Filters::new(1_000);
        filters.changed_none();
        assert_eq!(*total(&filters), ScanTotal { count: 0, sum: 0.0 });

        filters.write_then_changed_all();

        let mut expected: Vec<f32> = (0..1_000).map(|i| (i % 97) as f32 + DELTA).collect();
        expected.sort_by(f32::total_cmp);
        assert_eq!(sorted_tracked_x(&mut filters), expected);
        // Every x is a multiple of 2^-29 below 128, so the f64 sum is exact in any order.
        let sum = expected.iter().map(|&x| f64::from(x)).sum();
        assert_eq!(*total(&filters), ScanTotal { count: 1_000, sum });
    }

    #[test]
    fn optional_scan_reads_acceleration_on_even_entities_only() {
        let mut filters = Filters::new(1_000);
        filters.optional_half_present();
        // Positions sum to 46,995; 500 even entities add 0.25 each.
        assert_eq!(
            *total(&filters),
            ScanTotal {
                count: 1_000,
                sum: 47_120.0
            }
        );
    }
}
