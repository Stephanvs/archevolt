//! Bevy counterparts of Archevolt's benchmark workloads. This module holds the dense-movement
//! workload from `ReferenceComparisonBenchmarks`: the same component layout, initial values, and
//! `position += velocity * (1 / 60)` update. [`lifecycle`] and [`matrix`] mirror the entity
//! lifecycle, traversal, and filter workloads.
//!
//! `cargo bench` compiles for baseline x86-64, while the .NET JIT targets the host CPU; run
//! `RUSTFLAGS="-C target-cpu=native" cargo bench` to match it. The target decides how LLVM
//! vectorizes the handwritten and `for_each` loops across entities, which changes those results
//! severalfold. The JIT-compiled .NET loops are scalar.

use bevy_ecs::prelude::*;
use bevy_ecs::schedule::SingleThreadedExecutor;
use bevy_ecs::system::ScheduleSystem;

pub mod lifecycle;
pub mod matrix;

const DELTA: f32 = 1.0 / 60.0;

#[derive(Component, Clone, Copy, Debug, PartialEq)]
pub struct Position {
    x: f32,
    y: f32,
    z: f32,
}

#[derive(Component, Clone, Copy, Debug, PartialEq)]
pub struct Velocity {
    x: f32,
    y: f32,
    z: f32,
}

#[derive(Resource)]
pub struct FrameTime(f32);

impl Position {
    fn advanced(self, velocity: &Velocity, delta: f32) -> Self {
        Self {
            x: self.x + velocity.x * delta,
            y: self.y + velocity.y * delta,
            z: self.z + velocity.z * delta,
        }
    }
}

pub fn particle(index: usize) -> (Position, Velocity) {
    (
        Position {
            x: (index % 97) as f32,
            y: 0.0,
            z: 0.0,
        },
        Velocity {
            x: 1.0,
            y: 2.0,
            z: 3.0,
        },
    )
}

/// Per-table slices, the counterpart of ROADMAP step 1's slice-per-storage-block system. Writing
/// through the mutable slice marks every row in it as changed.
pub fn contiguous(mut query: Query<(&mut Position, &Velocity)>, time: Res<FrameTime>) {
    let delta = time.0;
    let tables = query
        .contiguous_iter_mut()
        .expect("Position and Velocity are table components");
    for (mut positions, velocities) in tables {
        for (position, velocity) in positions.iter_mut().zip(velocities) {
            *position = position.advanced(velocity, delta);
        }
    }
}

/// Internal iteration, the counterpart of Archevolt's generated `[Each]` loop.
pub fn for_each(mut query: Query<(&mut Position, &Velocity)>, time: Res<FrameTime>) {
    let delta = time.0;
    query.iter_mut().for_each(|(mut position, velocity)| {
        *position = position.advanced(velocity, delta);
    });
}

/// External iteration, the counterpart of a `foreach` over Archevolt's named query.
pub fn for_loop(mut query: Query<(&mut Position, &Velocity)>, time: Res<FrameTime>) {
    let delta = time.0;
    for (mut position, velocity) in &mut query {
        *position = position.advanced(velocity, delta);
    }
}

pub struct Handwritten {
    positions: Vec<Position>,
    velocities: Vec<Velocity>,
}

impl Handwritten {
    pub fn new(count: usize) -> Self {
        let (positions, velocities) = (0..count).map(particle).unzip();
        Self {
            positions,
            velocities,
        }
    }

    pub fn run(&mut self) {
        for (position, velocity) in self.positions.iter_mut().zip(&self.velocities) {
            *position = position.advanced(velocity, DELTA);
        }
    }
}

pub struct Scheduled {
    world: World,
    schedule: Schedule,
}

/// A schedule holding one system. Archevolt's benchmarks run with a concurrency of one.
pub fn single_threaded<M>(system: impl IntoScheduleConfigs<ScheduleSystem, M>) -> Schedule {
    let mut schedule = Schedule::default();
    schedule.set_executor(SingleThreadedExecutor::new());
    schedule.add_systems(system);
    schedule
}

impl Scheduled {
    pub fn new<M>(count: usize, system: impl IntoScheduleConfigs<ScheduleSystem, M>) -> Self {
        let mut world = World::new();
        world.spawn_batch((0..count).map(particle));
        Self::from_world(world, system)
    }

    pub fn from_world<M>(
        mut world: World,
        system: impl IntoScheduleConfigs<ScheduleSystem, M>,
    ) -> Self {
        world.insert_resource(FrameTime(DELTA));
        Self {
            world,
            schedule: single_threaded(system),
        }
    }

    pub fn run(&mut self) {
        self.schedule.run(&mut self.world);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn positions(scheduled: &mut Scheduled) -> Vec<Position> {
        let mut query = scheduled.world.query::<&Position>();
        query.iter(&scheduled.world).copied().collect()
    }

    #[test]
    fn bevy_systems_advance_every_entity_like_the_handwritten_loop() {
        let mut expected = Handwritten::new(1_000);
        expected.run();
        let mut each = Scheduled::new(1_000, for_each);
        each.run();
        let mut query = Scheduled::new(1_000, for_loop);
        query.run();
        let mut slices = Scheduled::new(1_000, contiguous);
        slices.run();

        assert_eq!(positions(&mut each), expected.positions);
        assert_eq!(positions(&mut query), expected.positions);
        assert_eq!(positions(&mut slices), expected.positions);
    }
}
