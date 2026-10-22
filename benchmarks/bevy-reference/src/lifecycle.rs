//! Bevy counterparts of Archevolt's `EntityPopulationBenchmarks` and `EntityStorageBenchmarks`.
//! Every operation calls `World` directly, as the Archevolt cases do.

use bevy_ecs::prelude::*;

use crate::matrix::{Tag0, WidePayload};
use crate::{Position, Velocity, particle};

pub const COUNTS: [usize; 2] = [1_000, 100_000];

/// A world with the workload's components registered, as building an Archevolt world registers
/// its module's components. Bevy creates the particle archetype and table on the first spawn;
/// Archevolt creates them when the world is built.
pub fn empty_world() -> World {
    let mut world = World::new();
    world.register_component::<Position>();
    world.register_component::<Velocity>();
    world.register_component::<Tag0>();
    world.register_component::<WidePayload>();
    world
}

/// Spawns particle `i` as entity `i`, one at a time, like `MatrixPopulation.Populate`.
pub fn populated(count: usize) -> (World, Vec<Entity>) {
    let mut world = empty_world();
    let mut entities = Vec::with_capacity(count);
    spawn(&mut world, &mut entities, count);
    (world, entities)
}

/// Fisher-Yates driven by SplitMix64 from seed 0: the order `MatrixPopulation.Shuffled` produces.
pub fn shuffled(entities: &[Entity]) -> Vec<Entity> {
    let mut result = entities.to_vec();
    let mut state: u64 = 0;
    for i in (1..result.len()).rev() {
        state = state.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = state;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        let j = ((z ^ (z >> 31)) % (i as u64 + 1)) as usize;
        result.swap(i, j);
    }
    result
}

pub fn spawn(world: &mut World, entities: &mut Vec<Entity>, count: usize) {
    for i in 0..count {
        entities.push(world.spawn(particle(i)).id());
    }
}

/// Archevolt has no batch spawn until ROADMAP step 5; its `Spawn` case is the batch baseline.
pub fn spawn_batch(world: &mut World, entities: &mut Vec<Entity>, count: usize) {
    entities.extend(world.spawn_batch((0..count).map(particle)));
}

pub fn despawn(world: &mut World, entities: &[Entity]) {
    for &entity in entities {
        world.despawn(entity);
    }
}

pub fn get(world: &World, entities: &[Entity]) -> f64 {
    let mut sum = 0.0;
    for &entity in entities {
        sum += f64::from(world.get::<Position>(entity).expect("live particle").x);
    }
    sum
}

pub fn tag_round_trip(world: &mut World, entities: &[Entity]) {
    for &entity in entities {
        world.entity_mut(entity).insert(Tag0);
        world.entity_mut(entity).remove::<Tag0>();
    }
}

pub fn wide_round_trip(world: &mut World, entities: &[Entity]) {
    for (i, &entity) in entities.iter().enumerate() {
        world.entity_mut(entity).insert(WidePayload::new(i as i32));
        world.entity_mut(entity).remove::<WidePayload>();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn assert_particles(world: &World, entities: &[Entity]) {
        for (i, &entity) in entities.iter().enumerate() {
            let (position, velocity) = particle(i);
            assert_eq!(world.get::<Position>(entity), Some(&position));
            assert_eq!(world.get::<Velocity>(entity), Some(&velocity));
            assert!(world.get::<Tag0>(entity).is_none());
            assert!(world.get::<WidePayload>(entity).is_none());
        }
    }

    #[test]
    fn shuffle_matches_the_archevolt_order() {
        let mut world = World::new();
        let entities: Vec<Entity> = (0..10).map(|_| world.spawn_empty().id()).collect();
        let order: Vec<usize> = shuffled(&entities)
            .iter()
            .map(|entity| entities.iter().position(|e| e == entity).unwrap())
            .collect();
        // Printed by MatrixPopulation.Shuffled over 0..10.
        assert_eq!(order, [6, 3, 2, 9, 8, 1, 4, 7, 0, 5]);
    }

    #[test]
    fn spawn_and_spawn_batch_give_entity_i_particle_i() {
        let (world, entities) = populated(1_000);
        assert_particles(&world, &entities);

        let mut world = empty_world();
        let mut entities = Vec::new();
        spawn_batch(&mut world, &mut entities, 1_000);
        assert_eq!(entities.len(), 1_000);
        assert_particles(&world, &entities);
    }

    #[test]
    fn despawn_removes_every_entity() {
        let (mut world, entities) = populated(1_000);
        despawn(&mut world, &shuffled(&entities));
        assert!(
            entities
                .iter()
                .all(|&entity| world.get_entity(entity).is_err())
        );
        assert_eq!(world.query::<&Position>().iter(&world).count(), 0);
    }

    #[test]
    fn get_reads_every_spawned_position() {
        let (world, entities) = populated(1_000);
        assert_eq!(get(&world, &shuffled(&entities)), 46_995.0);
    }

    #[test]
    fn round_trips_leave_the_population_unchanged() {
        let (mut world, entities) = populated(1_000);
        tag_round_trip(&mut world, &entities);
        wide_round_trip(&mut world, &entities);
        assert_particles(&world, &entities);
    }
}
